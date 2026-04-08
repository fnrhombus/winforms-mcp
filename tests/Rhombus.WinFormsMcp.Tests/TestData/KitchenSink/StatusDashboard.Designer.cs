namespace KitchenSink;

partial class StatusDashboard {
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing) {
        if (disposing && (components != null)) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent() {
        this.lblHeader = new System.Windows.Forms.Label();
        this.pnlGreen = new System.Windows.Forms.Panel();
        this.lblGreen = new System.Windows.Forms.Label();
        this.pnlYellow = new System.Windows.Forms.Panel();
        this.lblYellow = new System.Windows.Forms.Label();
        this.pnlRed = new System.Windows.Forms.Panel();
        this.lblRed = new System.Windows.Forms.Label();
        this.SuspendLayout();
        //
        // lblHeader
        //
        this.lblHeader.AutoSize = true;
        this.lblHeader.Font = new System.Drawing.Font("Consolas", 9F, System.Drawing.FontStyle.Bold);
        this.lblHeader.ForeColor = System.Drawing.Color.Lime;
        this.lblHeader.Location = new System.Drawing.Point(8, 4);
        this.lblHeader.Name = "lblHeader";
        this.lblHeader.Size = new System.Drawing.Size(105, 14);
        this.lblHeader.TabIndex = 0;
        this.lblHeader.Text = "SYSTEM STATUS";
        //
        // pnlGreen
        //
        this.pnlGreen.BackColor = System.Drawing.Color.LimeGreen;
        this.pnlGreen.Location = new System.Drawing.Point(8, 28);
        this.pnlGreen.Name = "pnlGreen";
        this.pnlGreen.Size = new System.Drawing.Size(16, 16);
        this.pnlGreen.TabIndex = 1;
        //
        // lblGreen
        //
        this.lblGreen.AutoSize = true;
        this.lblGreen.Font = new System.Drawing.Font("Consolas", 8F);
        this.lblGreen.ForeColor = System.Drawing.Color.LightGray;
        this.lblGreen.Location = new System.Drawing.Point(30, 28);
        this.lblGreen.Name = "lblGreen";
        this.lblGreen.Size = new System.Drawing.Size(91, 13);
        this.lblGreen.TabIndex = 2;
        this.lblGreen.Text = "Build: passing";
        //
        // pnlYellow
        //
        this.pnlYellow.BackColor = System.Drawing.Color.Gold;
        this.pnlYellow.Location = new System.Drawing.Point(8, 48);
        this.pnlYellow.Name = "pnlYellow";
        this.pnlYellow.Size = new System.Drawing.Size(16, 16);
        this.pnlYellow.TabIndex = 3;
        //
        // lblYellow
        //
        this.lblYellow.AutoSize = true;
        this.lblYellow.Font = new System.Drawing.Font("Consolas", 8F);
        this.lblYellow.ForeColor = System.Drawing.Color.LightGray;
        this.lblYellow.Location = new System.Drawing.Point(30, 48);
        this.lblYellow.Name = "lblYellow";
        this.lblYellow.Size = new System.Drawing.Size(79, 13);
        this.lblYellow.TabIndex = 4;
        this.lblYellow.Text = "Tests: flaky";
        //
        // pnlRed
        //
        this.pnlRed.BackColor = System.Drawing.Color.Red;
        this.pnlRed.Location = new System.Drawing.Point(150, 28);
        this.pnlRed.Name = "pnlRed";
        this.pnlRed.Size = new System.Drawing.Size(16, 16);
        this.pnlRed.TabIndex = 5;
        //
        // lblRed
        //
        this.lblRed.AutoSize = true;
        this.lblRed.Font = new System.Drawing.Font("Consolas", 8F);
        this.lblRed.ForeColor = System.Drawing.Color.LightGray;
        this.lblRed.Location = new System.Drawing.Point(172, 28);
        this.lblRed.Name = "lblRed";
        this.lblRed.Size = new System.Drawing.Size(67, 13);
        this.lblRed.TabIndex = 6;
        this.lblRed.Text = "Morale: 0%";
        //
        // StatusDashboard
        //
        this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(40)))));
        this.Controls.Add(this.lblHeader);
        this.Controls.Add(this.pnlGreen);
        this.Controls.Add(this.lblGreen);
        this.Controls.Add(this.pnlYellow);
        this.Controls.Add(this.lblYellow);
        this.Controls.Add(this.pnlRed);
        this.Controls.Add(this.lblRed);
        this.Name = "StatusDashboard";
        this.Size = new System.Drawing.Size(280, 80);
        this.ResumeLayout(false);
        this.PerformLayout();
    }

    private System.Windows.Forms.Label lblHeader;
    private System.Windows.Forms.Panel pnlGreen;
    private System.Windows.Forms.Label lblGreen;
    private System.Windows.Forms.Panel pnlYellow;
    private System.Windows.Forms.Label lblYellow;
    private System.Windows.Forms.Panel pnlRed;
    private System.Windows.Forms.Label lblRed;
}
