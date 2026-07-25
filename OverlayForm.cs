using System;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.VisualBasic.Devices;

namespace JnnBoost
{
    public class OverlayForm : Form
    {
        private Label lblInfo = null!;
        private System.Windows.Forms.Timer timer = null!;
        private PerformanceCounter cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");

        public OverlayForm()
        {
            InitUI();
        }

        private void InitUI()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.TopMost = true;
            this.Size = new Size(220, 60);
            this.Location = new Point(20, 20);
            this.BackColor = Color.FromArgb(28, 28, 48);
            this.Opacity = 0.92;

            lblInfo = new Label
            {
                AutoSize = false,
                Size = new Size(220, 60),
                Location = new Point(0, 0),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Consolas", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 212, 255),
                BackColor = Color.Transparent
            };

            this.Controls.Add(lblInfo);

            timer = new System.Windows.Forms.Timer { Interval = 1000 };
            // Qualified to avoid ambiguity between System.Windows.Forms.Timer and System.Threading.Timer (resolve CS0104)
            timer.Tick += (s, e) => UpdateInfo();
            cpuCounter.NextValue();
            timer.Start();
            UpdateInfo();
        }

        private void UpdateInfo()
        {
            try
            {
                float cpu = cpuCounter.NextValue();
                var info = new ComputerInfo();
                float total = info.TotalPhysicalMemory;
                float free = info.AvailablePhysicalMemory;
                float ram = ((total - free) / total) * 100;
                lblInfo.Text = $"CPU: {cpu:0}%   RAM: {ram:0}%";
            }
            catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { timer?.Stop(); cpuCounter?.Dispose(); } catch { }
            base.OnFormClosing(e);
        }
    }
}
