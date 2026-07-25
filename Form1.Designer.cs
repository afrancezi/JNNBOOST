using System.Windows.Forms;
using System.Drawing;
using System.Reflection;
namespace JnnBoost
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            pictureBox1 = new PictureBox();
            label1 = new Label();
            textBoxLog = new RichTextBox();
            button1 = new Button();
            button2 = new Button();
            button3 = new Button();
            button4 = new Button();
            button5 = new Button();
            button6 = new Button();
            button7 = new Button();
            button8 = new Button();
            button9 = new Button();
            button10 = new Button();
            button11 = new Button();
            progressBar1 = new ProgressBar();
            labelStatus = new Label();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).BeginInit();
            SuspendLayout();

            // pictureBox1
            pictureBox1.Image = (Image)resources.GetObject("pictureBox1.Image");
            pictureBox1.Location = new Point(109, 22);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new Size(250, 250);
            pictureBox1.TabIndex = 0;
            pictureBox1.TabStop = false;
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox1.BackColor = Color.FromArgb(26, 26, 46);

            // label1
            label1.AutoSize = true;
            label1.Font = new Font("Consolas", 10F, FontStyle.Bold);
            label1.ForeColor = Color.FromArgb(0, 212, 255);
            label1.Location = new Point(50, 280);
            label1.Name = "label1";
            label1.TabIndex = 1;
            label1.Text = "CPU: --%   RAM: --%";

            // labelStatus
            labelStatus.AutoSize = true;
            labelStatus.Font = new Font("Consolas", 8F);
            labelStatus.ForeColor = Color.FromArgb(90, 110, 130);
            labelStatus.Location = new Point(50, 644);
            labelStatus.Name = "labelStatus";
            labelStatus.TabIndex = 11;
            labelStatus.Text = "";

            // labelInlineNotification - pequena notificação discreta no UI
            labelInlineNotification = new Label();
            labelInlineNotification.AutoSize = false;
            labelInlineNotification.Size = new Size(300, 28);
            labelInlineNotification.Location = new Point(120, 22);
            labelInlineNotification.BackColor = Color.FromArgb(18, 24, 36);
            labelInlineNotification.ForeColor = Color.FromArgb(255, 165, 80);
            labelInlineNotification.Padding = new Padding(8, 6, 8, 6);
            labelInlineNotification.Visible = false;
            labelInlineNotification.Name = "labelInlineNotification";
            labelInlineNotification.Font = new Font("Consolas", 9F, FontStyle.Bold);

            // progressBar1
            progressBar1.ForeColor = Color.FromArgb(0, 212, 255);
            progressBar1.BackColor = Color.FromArgb(10, 10, 22);
            progressBar1.Location = new Point(50, 658);
            progressBar1.Name = "progressBar1";
            progressBar1.Size = new Size(350, 8);
            progressBar1.Style = ProgressBarStyle.Continuous;
            progressBar1.TabIndex = 12;

            // textBoxLog
            textBoxLog.BackColor = Color.FromArgb(10, 10, 22);
            textBoxLog.ForeColor = Color.FromArgb(0, 212, 255);
            textBoxLog.Location = new Point(50, 672);
            textBoxLog.Name = "textBoxLog";
            textBoxLog.ReadOnly = true;
            textBoxLog.ScrollBars = RichTextBoxScrollBars.Vertical;
            textBoxLog.Size = new Size(350, 185);
            textBoxLog.TabIndex = 2;
            textBoxLog.Text = "";
            textBoxLog.BorderStyle = BorderStyle.FixedSingle;
            textBoxLog.Font = new Font("Consolas", 8.5F);

            // panelConfirm - confirmação inline (esconde MessageBox)
            panelConfirm = new Panel();
            panelConfirm.Size = new Size(380, 120);
            panelConfirm.Location = new Point(50, 340);
            panelConfirm.BackColor = Color.FromArgb(15, 20, 30);
            panelConfirm.BorderStyle = BorderStyle.FixedSingle;
            panelConfirm.Visible = false;

            labelConfirmText = new Label();
            labelConfirmText.AutoSize = false;
            labelConfirmText.Size = new Size(360, 60);
            labelConfirmText.Location = new Point(10, 10);
            labelConfirmText.ForeColor = Color.FromArgb(200, 200, 200);
            labelConfirmText.Font = new Font("Consolas", 9F);

            btnConfirmYes = new Button();
            btnConfirmYes.Text = "Sim";
            btnConfirmYes.Size = new Size(80, 28);
            btnConfirmYes.Location = new Point(200, 75);

            btnConfirmNo = new Button();
            btnConfirmNo.Text = "Não";
            btnConfirmNo.Size = new Size(80, 28);
            btnConfirmNo.Location = new Point(290, 75);

            panelConfirm.Controls.Add(labelConfirmText);
            panelConfirm.Controls.Add(btnConfirmYes);
            panelConfirm.Controls.Add(btnConfirmNo);

            // button1 - FPS Boost
            button1.BackColor = Color.FromArgb(22, 33, 62);
            button1.ForeColor = Color.FromArgb(0, 212, 255);
            button1.FlatStyle = FlatStyle.Flat;
            button1.FlatAppearance.BorderColor = Color.FromArgb(0, 150, 200);
            button1.FlatAppearance.BorderSize = 1;
            button1.Font = new Font("Consolas", 8.5F, FontStyle.Bold);
            button1.Location = new Point(170, 309);
            button1.Name = "button1";
            button1.Size = new Size(110, 28);
            button1.TabIndex = 3;
            button1.Text = "FPS Boost";
            button1.UseVisualStyleBackColor = false;
            button1.Cursor = Cursors.Hand;
            button1.Click += button1_Click;

            // button2 - GPU Boost
            button2.BackColor = Color.FromArgb(22, 33, 62);
            button2.ForeColor = Color.FromArgb(0, 212, 255);
            button2.FlatStyle = FlatStyle.Flat;
            button2.FlatAppearance.BorderColor = Color.FromArgb(0, 150, 200);
            button2.FlatAppearance.BorderSize = 1;
            button2.Font = new Font("Consolas", 8.5F, FontStyle.Bold);
            button2.Location = new Point(170, 343);
            button2.Name = "button2";
            button2.Size = new Size(110, 28);
            button2.TabIndex = 4;
            button2.Text = "GPU Boost";
            button2.UseVisualStyleBackColor = false;
            button2.Cursor = Cursors.Hand;
            button2.Click += button2_Click;

            // button3 - Otimizar RAM
            button3.BackColor = Color.FromArgb(22, 33, 62);
            button3.ForeColor = Color.FromArgb(0, 212, 255);
            button3.FlatStyle = FlatStyle.Flat;
            button3.FlatAppearance.BorderColor = Color.FromArgb(0, 150, 200);
            button3.FlatAppearance.BorderSize = 1;
            button3.Font = new Font("Consolas", 8.5F, FontStyle.Bold);
            button3.Location = new Point(170, 377);
            button3.Name = "button3";
            button3.Size = new Size(110, 28);
            button3.TabIndex = 5;
            button3.Text = "Otimizar RAM";
            button3.UseVisualStyleBackColor = false;
            button3.Cursor = Cursors.Hand;
            button3.Click += button3_Click;

            // button4 - Limpar TEMP
            button4.BackColor = Color.FromArgb(22, 33, 62);
            button4.ForeColor = Color.FromArgb(0, 212, 255);
            button4.FlatStyle = FlatStyle.Flat;
            button4.FlatAppearance.BorderColor = Color.FromArgb(0, 150, 200);
            button4.FlatAppearance.BorderSize = 1;
            button4.Font = new Font("Consolas", 8.5F, FontStyle.Bold);
            button4.Location = new Point(170, 411);
            button4.Name = "button4";
            button4.Size = new Size(110, 28);
            button4.TabIndex = 6;
            button4.Text = "Limpar TEMP";
            button4.UseVisualStyleBackColor = false;
            button4.Cursor = Cursors.Hand;
            button4.Click += button4_Click;

            // button5 - Limpar Rede
            button5.BackColor = Color.FromArgb(22, 33, 62);
            button5.ForeColor = Color.FromArgb(0, 212, 255);
            button5.FlatStyle = FlatStyle.Flat;
            button5.FlatAppearance.BorderColor = Color.FromArgb(0, 150, 200);
            button5.FlatAppearance.BorderSize = 1;
            button5.Font = new Font("Consolas", 8.5F, FontStyle.Bold);
            button5.Location = new Point(170, 445);
            button5.Name = "button5";
            button5.Size = new Size(110, 28);
            button5.TabIndex = 7;
            button5.Text = "Limpar Rede";
            button5.UseVisualStyleBackColor = false;
            button5.Cursor = Cursors.Hand;
            button5.Click += button5_Click;

            // button6 - Diagnóstico (cor especial)
            button6.BackColor = Color.FromArgb(10, 50, 80);
            button6.ForeColor = Color.FromArgb(0, 212, 255);
            button6.FlatStyle = FlatStyle.Flat;
            button6.FlatAppearance.BorderColor = Color.FromArgb(0, 150, 200);
            button6.FlatAppearance.BorderSize = 1;
            button6.Font = new Font("Consolas", 8.5F, FontStyle.Bold);
            button6.Location = new Point(170, 479);
            button6.Name = "button6";
            button6.Size = new Size(110, 28);
            button6.TabIndex = 8;
            button6.Text = "Diagnóstico";
            button6.UseVisualStyleBackColor = false;
            button6.Cursor = Cursors.Hand;
            button6.Click += button6_Click;

            // button7 - Otimizar Jogo (cor especial)
            button7.BackColor = Color.FromArgb(10, 50, 80);
            button7.ForeColor = Color.FromArgb(0, 212, 255);
            button7.FlatStyle = FlatStyle.Flat;
            button7.FlatAppearance.BorderColor = Color.FromArgb(0, 150, 200);
            button7.FlatAppearance.BorderSize = 1;
            button7.Font = new Font("Consolas", 8.5F, FontStyle.Bold);
            button7.Location = new Point(170, 513);
            button7.Name = "button7";
            button7.Size = new Size(110, 28);
            button7.TabIndex = 9;
            button7.Text = "Otimizar Jogo";
            button7.UseVisualStyleBackColor = false;
            button7.Cursor = Cursors.Hand;
            button7.Click += button7_Click;

            // button8 - Analisar Gargalo (cor especial)
            button8.BackColor = Color.FromArgb(10, 50, 80);
            button8.ForeColor = Color.FromArgb(0, 212, 255);
            button8.FlatStyle = FlatStyle.Flat;
            button8.FlatAppearance.BorderColor = Color.FromArgb(0, 150, 200);
            button8.FlatAppearance.BorderSize = 1;
            button8.Font = new Font("Consolas", 8.5F, FontStyle.Bold);
            button8.Location = new Point(170, 547);
            button8.Name = "button8";
            button8.Size = new Size(110, 28);
            button8.TabIndex = 10;
            button8.Text = "Analisar Gargalo";
            button8.UseVisualStyleBackColor = false;
            button8.Cursor = Cursors.Hand;
            button8.Click += button8_Click;

            // button9 - Restaurar Padrões (cor verde escuro)
            button9.BackColor = Color.FromArgb(15, 35, 15);
            button9.ForeColor = Color.FromArgb(0, 200, 90);
            button9.FlatStyle = FlatStyle.Flat;
            button9.FlatAppearance.BorderColor = Color.FromArgb(0, 150, 70);
            button9.FlatAppearance.BorderSize = 1;
            button9.Font = new Font("Consolas", 8.5F, FontStyle.Bold);
            button9.Location = new Point(170, 581);
            button9.Name = "button9";
            button9.Size = new Size(110, 28);
            button9.TabIndex = 13;
            button9.Text = "Restaurar Padrões";
            button9.UseVisualStyleBackColor = false;
            button9.Cursor = Cursors.Hand;
            button9.Click += button9_Click;

            // button10 - Overlay
            button10.BackColor = Color.FromArgb(22, 33, 62);
            button10.ForeColor = Color.FromArgb(0, 212, 255);
            button10.FlatStyle = FlatStyle.Flat;
            button10.FlatAppearance.BorderColor = Color.FromArgb(0, 150, 200);
            button10.FlatAppearance.BorderSize = 1;
            button10.Font = new Font("Consolas", 8.5F, FontStyle.Bold);
            button10.Location = new Point(170, 615);
            button10.Name = "button10";
            button10.Size = new Size(110, 28);
            button10.TabIndex = 14;
            button10.Text = "Overlay";
            button10.UseVisualStyleBackColor = false;
            button10.Cursor = Cursors.Hand;
            button10.Click += button10_Click;

            // button11 - invisível/reservado
            button11.Location = new Point(0, 0);
            button11.Name = "button11";
            button11.Size = new Size(75, 23);
            button11.TabIndex = 15;
            button11.Visible = false;

            // Form1
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(26, 26, 46);
            ClientSize = new Size(461, 875);
            Text = "JnnBoost — Game Optimizer";
            Controls.Add(button11);
            Controls.Add(button10);
            Controls.Add(button9);
            Controls.Add(progressBar1);
            Controls.Add(labelStatus);
            Controls.Add(button8);
            Controls.Add(button7);
            Controls.Add(button6);
            Controls.Add(button5);
            Controls.Add(button4);
            Controls.Add(button3);
            Controls.Add(button2);
            Controls.Add(button1);
            Controls.Add(textBoxLog);
            Controls.Add(label1);
            Controls.Add(pictureBox1);
            Name = "Form1";
            Load += Form1_Load;
            // animation timer wiring (handled in runtime constructor)
            ((System.ComponentModel.ISupportInitialize)pictureBox1).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        private PictureBox pictureBox1;
        private Label label1;
        private Label labelStatus;
        private RichTextBox textBoxLog;
        private Button button1;
        private Button button2;
        private Button button3;
        private Button button4;
        private Button button5;
        private Button button6;
        private Button button7;
        private Button button8;
        private Button button9;
        private Button button10;
        private Button button11;
        private ProgressBar progressBar1;
        private Label labelInlineNotification;
        private Panel panelConfirm;
        private Label labelConfirmText;
        private Button btnConfirmYes;
        private Button btnConfirmNo;
    }
}