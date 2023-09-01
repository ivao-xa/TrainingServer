namespace TrainingServer;

partial class FrmMain
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
		tableLayoutPanel1 = new TableLayoutPanel();
		BtnStart = new Button();
		tableLayoutPanel1.SuspendLayout();
		SuspendLayout();
		// 
		// tableLayoutPanel1
		// 
		tableLayoutPanel1.ColumnCount = 2;
		tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 80F));
		tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
		tableLayoutPanel1.Controls.Add(BtnStart, 1, 1);
		tableLayoutPanel1.Dock = DockStyle.Fill;
		tableLayoutPanel1.Location = new Point(0, 0);
		tableLayoutPanel1.Name = "tableLayoutPanel1";
		tableLayoutPanel1.RowCount = 2;
		tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 90F));
		tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 10F));
		tableLayoutPanel1.Size = new Size(800, 450);
		tableLayoutPanel1.TabIndex = 0;
		// 
		// BtnStart
		// 
		BtnStart.Dock = DockStyle.Fill;
		BtnStart.Enabled = false;
		BtnStart.Location = new Point(643, 408);
		BtnStart.Name = "BtnStart";
		BtnStart.Size = new Size(154, 39);
		BtnStart.TabIndex = 0;
		BtnStart.Text = "Connect";
		BtnStart.UseVisualStyleBackColor = true;
		BtnStart.Click += BtnStart_Click;
		// 
		// FrmMain
		// 
		AutoScaleDimensions = new SizeF(7F, 15F);
		AutoScaleMode = AutoScaleMode.Font;
		ClientSize = new Size(800, 450);
		Controls.Add(tableLayoutPanel1);
		Name = "FrmMain";
		Text = "Training Server (Loading)";
		tableLayoutPanel1.ResumeLayout(false);
		ResumeLayout(false);
	}

	#endregion

	private TableLayoutPanel tableLayoutPanel1;
	private Button BtnStart;
}
