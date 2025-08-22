namespace SophonChunksDownloader
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            下载进度条 = new ProgressBar();
            下载游戏 = new Button();
            textBox1 = new TextBox();
            label1 = new Label();
            选择下载框 = new CheckedListBox();
            label2 = new Label();
            下载清单 = new Button();
            backgroundWorker1 = new System.ComponentModel.BackgroundWorker();
            SuspendLayout();
            // 
            // 下载进度条
            // 
            下载进度条.Location = new Point(22, 174);
            下载进度条.Name = "下载进度条";
            下载进度条.Size = new Size(631, 24);
            下载进度条.TabIndex = 0;
            // 
            // 下载游戏
            // 
            下载游戏.Enabled = false;
            下载游戏.Location = new Point(549, 55);
            下载游戏.Name = "下载游戏";
            下载游戏.Size = new Size(104, 30);
            下载游戏.TabIndex = 1;
            下载游戏.Text = "开始下载";
            下载游戏.UseVisualStyleBackColor = true;
            下载游戏.Click += 下载游戏_Click;
            // 
            // textBox1
            // 
            textBox1.Location = new Point(87, 23);
            textBox1.Name = "textBox1";
            textBox1.Size = new Size(455, 23);
            textBox1.TabIndex = 3;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(22, 26);
            label1.Name = "label1";
            label1.Size = new Size(59, 17);
            label1.TabIndex = 4;
            label1.Text = "下载地址:";
            // 
            // 选择下载框
            // 
            选择下载框.FormattingEnabled = true;
            选择下载框.Location = new Point(22, 55);
            选择下载框.Name = "选择下载框";
            选择下载框.Size = new Size(151, 112);
            选择下载框.TabIndex = 6;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(22, 210);
            label2.Name = "label2";
            label2.Size = new Size(32, 17);
            label2.TabIndex = 7;
            label2.Text = "状态";
            label2.Visible = false;
            // 
            // 下载清单
            // 
            下载清单.Location = new Point(549, 19);
            下载清单.Name = "下载清单";
            下载清单.Size = new Size(104, 30);
            下载清单.TabIndex = 8;
            下载清单.Text = "下载清单";
            下载清单.UseVisualStyleBackColor = true;
            下载清单.Click += 下载清单_Click;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(676, 251);
            Controls.Add(下载清单);
            Controls.Add(label2);
            Controls.Add(选择下载框);
            Controls.Add(label1);
            Controls.Add(textBox1);
            Controls.Add(下载游戏);
            Controls.Add(下载进度条);
            Name = "Form1";
            Text = "下载器";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private ProgressBar 下载进度条;
        private Button 下载游戏;
        private TextBox textBox1;
        private Label label1;
        private CheckedListBox 选择下载框;
        private Label label2;
        private Button 下载清单;
        private System.ComponentModel.BackgroundWorker backgroundWorker1;
    }
}
