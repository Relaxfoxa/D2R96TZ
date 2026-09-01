using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace D2R96TZ
{
    public sealed class StatusWindow : IDisposable
    {
        private readonly ManualResetEvent started = new ManualResetEvent(false);
        private Thread uiThread;
        private StatusForm form;

        public void Start()
        {
            uiThread = new Thread(RunUi);
            uiThread.IsBackground = true;
            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.Start();
            started.WaitOne(3000);
        }

        public void Update(string mode, string currentRoom, string trackedOwner, string trackingStatus, string action)
        {
            StatusForm current = form;
            if (current == null || current.IsDisposed || !current.IsHandleCreated) return;
            try
            {
                current.BeginInvoke(new Action(() => current.Apply(mode, currentRoom, trackedOwner, trackingStatus, action)));
            }
            catch (InvalidOperationException) { }
        }

        public void Dispose()
        {
            StatusForm current = form;
            if (current != null && !current.IsDisposed && current.IsHandleCreated)
            {
                try { current.BeginInvoke(new Action(current.Close)); }
                catch (InvalidOperationException) { }
            }
            if (uiThread != null && uiThread.IsAlive) uiThread.Join(1000);
            started.Dispose();
        }

        private void RunUi()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                form = new StatusForm();
                form.Show();
                form.BringToFront();
                form.Activate();
                started.Set();
                Application.Run(form);
            }
            catch (Exception ex)
            {
                try
                {
                    string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                    Directory.CreateDirectory(logDirectory);
                    File.AppendAllText(Path.Combine(logDirectory, "status-window-error.log"), DateTime.Now.ToString("O") + Environment.NewLine + ex + Environment.NewLine);
                }
                catch { }
                started.Set();
            }
        }

        private sealed class StatusForm : Form
        {
            private readonly Label modeValue;
            private readonly Label roomValue;
            private readonly Label ownerValue;
            private readonly Label trackingValue;
            private readonly Label actionValue;

            public StatusForm()
            {
                Text = "D2R96TZ 监听器";
                ClientSize = new Size(390, 205);
                FormBorderStyle = FormBorderStyle.FixedToolWindow;
                MaximizeBox = false;
                MinimizeBox = true;
                ShowInTaskbar = true;
                TopMost = true;
                StartPosition = FormStartPosition.CenterScreen;
                Shown += (sender, args) =>
                {
                    BringToFront();
                    Activate();
                };

                var layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    Padding = new Padding(10),
                    ColumnCount = 2,
                    RowCount = 6
                };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                for (int index = 0; index < 6; index++) layout.RowStyles.Add(new RowStyle(SizeType.Percent, 16.6667f));

                layout.Controls.Add(MakeCaption("状态"), 0, 0);
                layout.Controls.Add(MakeValue("监听中"), 1, 0);
                layout.Controls.Add(MakeCaption("模式"), 0, 1);
                modeValue = MakeValue("大厅关键词扫描");
                layout.Controls.Add(modeValue, 1, 1);
                layout.Controls.Add(MakeCaption("当前房间"), 0, 2);
                roomValue = MakeValue("无");
                layout.Controls.Add(roomValue, 1, 2);
                layout.Controls.Add(MakeCaption("跟踪玩家"), 0, 3);
                ownerValue = MakeValue("未知");
                layout.Controls.Add(ownerValue, 1, 3);
                layout.Controls.Add(MakeCaption("跟踪状态"), 0, 4);
                trackingValue = MakeValue("等待进入房间");
                layout.Controls.Add(trackingValue, 1, 4);
                layout.Controls.Add(MakeCaption("最近动作"), 0, 5);
                actionValue = MakeValue("等待 F8；F12 停止并清空");
                layout.Controls.Add(actionValue, 1, 5);
                Controls.Add(layout);
            }

            public void Apply(string mode, string currentRoom, string trackedOwner, string trackingStatus, string action)
            {
                modeValue.Text = mode ?? "未知";
                roomValue.Text = string.IsNullOrEmpty(currentRoom) ? "无" : currentRoom;
                ownerValue.Text = string.IsNullOrEmpty(trackedOwner) ? "未知" : trackedOwner;
                trackingValue.Text = trackingStatus ?? "未知";
                actionValue.Text = action ?? "—";
            }

            private static Label MakeCaption(string text)
            {
                return new Label
                {
                    Text = text,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    AutoEllipsis = true
                };
            }

            private static Label MakeValue(string text)
            {
                return new Label
                {
                    Text = text,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    AutoEllipsis = true
                };
            }
        }
    }
}
