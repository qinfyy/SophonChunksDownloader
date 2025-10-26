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
            暂停按钮 = new Button();
            游戏组合框 = new ComboBox();
            label3 = new Label();
            版本编辑框 = new TextBox();
            tableLayoutPanel1 = new TableLayoutPanel();
            panel1 = new Panel();
            清理多余文件 = new CheckBox();
            tableLayoutPanel1.SuspendLayout();
            panel1.SuspendLayout();
            SuspendLayout();
            // 
            // 下载进度条
            // 
            下载进度条.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            下载进度条.Location = new Point(22, 174);
            下载进度条.Name = "下载进度条";
            下载进度条.Size = new Size(631, 24);
            下载进度条.TabIndex = 0;
            // 
            // 下载游戏
            // 
            下载游戏.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            下载游戏.Enabled = false;
            下载游戏.Location = new Point(559, 88);
            下载游戏.Name = "下载游戏";
            下载游戏.Size = new Size(105, 28);
            下载游戏.TabIndex = 1;
            下载游戏.Text = "开始下载";
            下载游戏.UseVisualStyleBackColor = true;
            下载游戏.Click += 下载游戏_Click;
            // 
            // textBox1
            // 
            textBox1.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
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
            选择下载框.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            选择下载框.FormattingEnabled = true;
            选择下载框.Location = new Point(3, 3);
            选择下载框.Name = "选择下载框";
            选择下载框.Size = new Size(133, 94);
            选择下载框.TabIndex = 6;
            // 
            // label2
            // 
            label2.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
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
            下载清单.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            下载清单.Location = new Point(559, 18);
            下载清单.Name = "下载清单";
            下载清单.Size = new Size(105, 28);
            下载清单.TabIndex = 8;
            下载清单.Text = "下载清单";
            下载清单.UseVisualStyleBackColor = true;
            下载清单.Click += 下载清单_Click;
            // 
            // 暂停按钮
            // 
            暂停按钮.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            暂停按钮.Enabled = false;
            暂停按钮.Location = new Point(559, 52);
            暂停按钮.Name = "暂停按钮";
            暂停按钮.Size = new Size(105, 30);
            暂停按钮.TabIndex = 9;
            暂停按钮.Text = "暂停下载";
            暂停按钮.UseVisualStyleBackColor = true;
            暂停按钮.Click += 暂停按钮_Click;
            // 
            // 游戏组合框
            // 
            游戏组合框.FormattingEnabled = true;
            游戏组合框.Location = new Point(0, 3);
            游戏组合框.Name = "游戏组合框";
            游戏组合框.Size = new Size(180, 25);
            游戏组合框.TabIndex = 10;
            游戏组合框.SelectedIndexChanged += 游戏组合框_SelectedIndexChanged;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(3, 37);
            label3.Name = "label3";
            label3.Size = new Size(44, 17);
            label3.TabIndex = 11;
            label3.Text = "版本：";
            // 
            // 版本编辑框
            // 
            版本编辑框.Location = new Point(53, 34);
            版本编辑框.Name = "版本编辑框";
            版本编辑框.Size = new Size(65, 23);
            版本编辑框.TabIndex = 12;
            // 
            // tableLayoutPanel1
            // 
            tableLayoutPanel1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            tableLayoutPanel1.ColumnCount = 2;
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
            tableLayoutPanel1.Controls.Add(选择下载框, 0, 0);
            tableLayoutPanel1.Controls.Add(panel1, 1, 0);
            tableLayoutPanel1.Location = new Point(22, 52);
            tableLayoutPanel1.Name = "tableLayoutPanel1";
            tableLayoutPanel1.RowCount = 1;
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableLayoutPanel1.Size = new Size(348, 116);
            tableLayoutPanel1.TabIndex = 13;
            // 
            // panel1
            // 
            panel1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            panel1.Controls.Add(清理多余文件);
            panel1.Controls.Add(游戏组合框);
            panel1.Controls.Add(版本编辑框);
            panel1.Controls.Add(label3);
            panel1.Location = new Point(142, 3);
            panel1.Name = "panel1";
            panel1.Size = new Size(203, 110);
            panel1.TabIndex = 7;
            // 
            // 清理多余文件
            // 
            清理多余文件.AutoSize = true;
            清理多余文件.Location = new Point(5, 66);
            清理多余文件.Name = "清理多余文件";
            清理多余文件.Size = new Size(135, 21);
            清理多余文件.TabIndex = 13;
            清理多余文件.Text = "清理文件夹多余文件";
            清理多余文件.UseVisualStyleBackColor = true;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(676, 251);
            Controls.Add(下载清单);
            Controls.Add(下载游戏);
            Controls.Add(tableLayoutPanel1);
            Controls.Add(暂停按钮);
            Controls.Add(label2);
            Controls.Add(label1);
            Controls.Add(textBox1);
            Controls.Add(下载进度条);
            Name = "Form1";
            Text = "下载器";
            Load += Form1_Load;
            tableLayoutPanel1.ResumeLayout(false);
            panel1.ResumeLayout(false);
            panel1.PerformLayout();
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
        private Button 暂停按钮;
        private ComboBox 游戏组合框;
        private Label label3;
        private TextBox 版本编辑框;
        private TableLayoutPanel tableLayoutPanel1;
        private Panel panel1;
        private CheckBox 清理多余文件;
    }
}
