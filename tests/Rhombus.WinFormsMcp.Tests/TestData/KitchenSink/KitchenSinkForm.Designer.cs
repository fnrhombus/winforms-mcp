namespace KitchenSink;

partial class KitchenSinkForm {
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing) {
        if (disposing && (components != null)) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent() {
        this.lblTitle = new System.Windows.Forms.Label();
        this.grpDread = new System.Windows.Forms.GroupBox();
        this.lblFear = new System.Windows.Forms.Label();
        this.txtFear = new System.Windows.Forms.TextBox();
        this.chkAccept = new System.Windows.Forms.CheckBox();
        this.radBad = new System.Windows.Forms.RadioButton();
        this.radWorse = new System.Windows.Forms.RadioButton();
        this.nudLevel = new System.Windows.Forms.NumericUpDown();
        this.lblLevel = new System.Windows.Forms.Label();
        this.pnlChaos = new System.Windows.Forms.Panel();
        this.btnPanic = new System.Windows.Forms.Button();
        this.btnCalm = new System.Windows.Forms.Button();
        this.lnkHelp = new System.Windows.Forms.LinkLabel();
        this.picBox = new System.Windows.Forms.PictureBox();
        this.grpVoid = new System.Windows.Forms.GroupBox();
        this.cboMood = new System.Windows.Forms.ComboBox();
        this.lstBugs = new System.Windows.Forms.ListBox();
        this.clbTasks = new System.Windows.Forms.CheckedListBox();
        this.tabControl = new System.Windows.Forms.TabControl();
        this.tabSorrow = new System.Windows.Forms.TabPage();
        this.rtbNotes = new System.Windows.Forms.RichTextBox();
        this.tabDespair = new System.Windows.Forms.TabPage();
        this.dtpDoomsday = new System.Windows.Forms.DateTimePicker();
        this.lblDoomsday = new System.Windows.Forms.Label();
        this.trkVolume = new System.Windows.Forms.TrackBar();
        this.prgDoom = new System.Windows.Forms.ProgressBar();
        this.lblWarning = new System.Windows.Forms.Label();
        this.lblStatus = new System.Windows.Forms.Label();
        this.grpDread.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)(this.nudLevel)).BeginInit();
        this.pnlChaos.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)(this.picBox)).BeginInit();
        this.grpVoid.SuspendLayout();
        this.tabControl.SuspendLayout();
        this.tabSorrow.SuspendLayout();
        this.tabDespair.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)(this.trkVolume)).BeginInit();
        this.SuspendLayout();
        //
        // lblTitle
        //
        this.lblTitle.Font = new System.Drawing.Font("Segoe UI", 16F, System.Drawing.FontStyle.Bold);
        this.lblTitle.ForeColor = System.Drawing.Color.DarkRed;
        this.lblTitle.Location = new System.Drawing.Point(12, 9);
        this.lblTitle.Name = "lblTitle";
        this.lblTitle.Size = new System.Drawing.Size(920, 35);
        this.lblTitle.TabIndex = 0;
        this.lblTitle.Text = "The Kitchen Sink of Doom";
        //
        // grpDread
        //
        this.grpDread.Controls.Add(this.lblFear);
        this.grpDread.Controls.Add(this.txtFear);
        this.grpDread.Controls.Add(this.chkAccept);
        this.grpDread.Controls.Add(this.radBad);
        this.grpDread.Controls.Add(this.radWorse);
        this.grpDread.Controls.Add(this.nudLevel);
        this.grpDread.Controls.Add(this.lblLevel);
        this.grpDread.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
        this.grpDread.ForeColor = System.Drawing.Color.MidnightBlue;
        this.grpDread.Location = new System.Drawing.Point(12, 50);
        this.grpDread.Name = "grpDread";
        this.grpDread.Size = new System.Drawing.Size(290, 200);
        this.grpDread.TabIndex = 1;
        this.grpDread.TabStop = false;
        this.grpDread.Text = "Existential Dread";
        //
        // lblFear
        //
        this.lblFear.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.lblFear.ForeColor = System.Drawing.Color.Black;
        this.lblFear.Location = new System.Drawing.Point(12, 25);
        this.lblFear.Name = "lblFear";
        this.lblFear.Size = new System.Drawing.Size(260, 18);
        this.lblFear.TabIndex = 0;
        this.lblFear.Text = "Your deepest fear:";
        //
        // txtFear
        //
        this.txtFear.BackColor = System.Drawing.Color.LightYellow;
        this.txtFear.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.txtFear.ForeColor = System.Drawing.Color.DarkRed;
        this.txtFear.Location = new System.Drawing.Point(12, 46);
        this.txtFear.Name = "txtFear";
        this.txtFear.Size = new System.Drawing.Size(260, 23);
        this.txtFear.TabIndex = 1;
        this.txtFear.Text = "undefined is not a function";
        //
        // chkAccept
        //
        this.chkAccept.AutoSize = true;
        this.chkAccept.Checked = true;
        this.chkAccept.CheckState = System.Windows.Forms.CheckState.Checked;
        this.chkAccept.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.chkAccept.ForeColor = System.Drawing.Color.Black;
        this.chkAccept.Location = new System.Drawing.Point(12, 76);
        this.chkAccept.Name = "chkAccept";
        this.chkAccept.Size = new System.Drawing.Size(178, 19);
        this.chkAccept.TabIndex = 2;
        this.chkAccept.Text = "I accept the consequences";
        //
        // radBad
        //
        this.radBad.AutoSize = true;
        this.radBad.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.radBad.ForeColor = System.Drawing.Color.Black;
        this.radBad.Location = new System.Drawing.Point(12, 102);
        this.radBad.Name = "radBad";
        this.radBad.Size = new System.Drawing.Size(110, 19);
        this.radBad.TabIndex = 3;
        this.radBad.Text = "Option A (Bad)";
        //
        // radWorse
        //
        this.radWorse.AutoSize = true;
        this.radWorse.Checked = true;
        this.radWorse.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.radWorse.ForeColor = System.Drawing.Color.Black;
        this.radWorse.Location = new System.Drawing.Point(12, 126);
        this.radWorse.Name = "radWorse";
        this.radWorse.Size = new System.Drawing.Size(118, 19);
        this.radWorse.TabIndex = 4;
        this.radWorse.TabStop = true;
        this.radWorse.Text = "Option B (Worse)";
        //
        // nudLevel
        //
        this.nudLevel.BackColor = System.Drawing.Color.MistyRose;
        this.nudLevel.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.nudLevel.Location = new System.Drawing.Point(12, 158);
        this.nudLevel.Maximum = new decimal(new int[] { 100, 0, 0, 0 });
        this.nudLevel.Name = "nudLevel";
        this.nudLevel.Size = new System.Drawing.Size(80, 23);
        this.nudLevel.TabIndex = 5;
        this.nudLevel.Value = new decimal(new int[] { 42, 0, 0, 0 });
        //
        // lblLevel
        //
        this.lblLevel.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.lblLevel.ForeColor = System.Drawing.Color.Black;
        this.lblLevel.Location = new System.Drawing.Point(100, 160);
        this.lblLevel.Name = "lblLevel";
        this.lblLevel.Size = new System.Drawing.Size(120, 18);
        this.lblLevel.TabIndex = 6;
        this.lblLevel.Text = "Doom Level (0-100)";
        //
        // pnlChaos
        //
        this.pnlChaos.BackColor = System.Drawing.Color.LemonChiffon;
        this.pnlChaos.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
        this.pnlChaos.Controls.Add(this.btnPanic);
        this.pnlChaos.Controls.Add(this.btnCalm);
        this.pnlChaos.Controls.Add(this.lnkHelp);
        this.pnlChaos.Controls.Add(this.picBox);
        this.pnlChaos.Location = new System.Drawing.Point(312, 50);
        this.pnlChaos.Name = "pnlChaos";
        this.pnlChaos.Size = new System.Drawing.Size(310, 200);
        this.pnlChaos.TabIndex = 2;
        //
        // btnPanic
        //
        this.btnPanic.BackColor = System.Drawing.Color.Tomato;
        this.btnPanic.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnPanic.Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Bold);
        this.btnPanic.ForeColor = System.Drawing.Color.White;
        this.btnPanic.Location = new System.Drawing.Point(10, 10);
        this.btnPanic.Name = "btnPanic";
        this.btnPanic.Size = new System.Drawing.Size(135, 40);
        this.btnPanic.TabIndex = 0;
        this.btnPanic.Text = "PANIC!";
        this.btnPanic.UseVisualStyleBackColor = false;
        //
        // btnCalm
        //
        this.btnCalm.BackColor = System.Drawing.Color.MediumSeaGreen;
        this.btnCalm.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        this.btnCalm.Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Bold);
        this.btnCalm.ForeColor = System.Drawing.Color.White;
        this.btnCalm.Location = new System.Drawing.Point(155, 10);
        this.btnCalm.Name = "btnCalm";
        this.btnCalm.Size = new System.Drawing.Size(135, 40);
        this.btnCalm.TabIndex = 1;
        this.btnCalm.Text = "Stay Calm";
        this.btnCalm.UseVisualStyleBackColor = false;
        //
        // lnkHelp
        //
        this.lnkHelp.AutoSize = true;
        this.lnkHelp.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Italic);
        this.lnkHelp.Location = new System.Drawing.Point(10, 58);
        this.lnkHelp.Name = "lnkHelp";
        this.lnkHelp.Size = new System.Drawing.Size(194, 15);
        this.lnkHelp.TabIndex = 2;
        this.lnkHelp.TabStop = true;
        this.lnkHelp.Text = "Click for help (there is none)";
        //
        // picBox
        //
        this.picBox.BackColor = System.Drawing.Color.DarkSlateBlue;
        this.picBox.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
        this.picBox.Location = new System.Drawing.Point(10, 82);
        this.picBox.Name = "picBox";
        this.picBox.Size = new System.Drawing.Size(288, 106);
        this.picBox.TabIndex = 3;
        this.picBox.TabStop = false;
        //
        // grpVoid
        //
        this.grpVoid.Controls.Add(this.cboMood);
        this.grpVoid.Controls.Add(this.lstBugs);
        this.grpVoid.Controls.Add(this.clbTasks);
        this.grpVoid.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
        this.grpVoid.ForeColor = System.Drawing.Color.Indigo;
        this.grpVoid.Location = new System.Drawing.Point(632, 50);
        this.grpVoid.Name = "grpVoid";
        this.grpVoid.Size = new System.Drawing.Size(306, 200);
        this.grpVoid.TabIndex = 3;
        this.grpVoid.TabStop = false;
        this.grpVoid.Text = "The Void Stares Back";
        //
        // cboMood
        //
        this.cboMood.BackColor = System.Drawing.Color.Lavender;
        this.cboMood.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        this.cboMood.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.cboMood.Items.AddRange(new object[] { "Monday Blues", "Compiler Error", "Segfault", "Production Bug at 3 AM" });
        this.cboMood.Location = new System.Drawing.Point(12, 25);
        this.cboMood.Name = "cboMood";
        this.cboMood.Size = new System.Drawing.Size(280, 23);
        this.cboMood.TabIndex = 0;
        //
        // lstBugs
        //
        this.lstBugs.BackColor = System.Drawing.Color.Honeydew;
        this.lstBugs.Font = new System.Drawing.Font("Consolas", 9F);
        this.lstBugs.ForeColor = System.Drawing.Color.DarkGreen;
        this.lstBugs.Items.AddRange(new object[] { "Bug #404: Not Found", "Bug #500: Gave Up", "Bug #418: I'm a Teapot", "Bug #451: Censored" });
        this.lstBugs.Location = new System.Drawing.Point(12, 56);
        this.lstBugs.Name = "lstBugs";
        this.lstBugs.Size = new System.Drawing.Size(138, 134);
        this.lstBugs.TabIndex = 1;
        //
        // clbTasks
        //
        this.clbTasks.BackColor = System.Drawing.Color.OldLace;
        this.clbTasks.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.clbTasks.ForeColor = System.Drawing.Color.SaddleBrown;
        this.clbTasks.Items.AddRange(new object[] { "Write tests", "Fix tests", "Delete tests", "Cry softly" });
        this.clbTasks.Location = new System.Drawing.Point(158, 56);
        this.clbTasks.Name = "clbTasks";
        this.clbTasks.Size = new System.Drawing.Size(136, 134);
        this.clbTasks.TabIndex = 2;
        //
        // tabControl
        //
        this.tabControl.Controls.Add(this.tabSorrow);
        this.tabControl.Controls.Add(this.tabDespair);
        this.tabControl.Location = new System.Drawing.Point(12, 260);
        this.tabControl.Name = "tabControl";
        this.tabControl.SelectedIndex = 0;
        this.tabControl.Size = new System.Drawing.Size(926, 200);
        this.tabControl.TabIndex = 4;
        //
        // tabSorrow
        //
        this.tabSorrow.BackColor = System.Drawing.Color.AliceBlue;
        this.tabSorrow.Controls.Add(this.rtbNotes);
        this.tabSorrow.Location = new System.Drawing.Point(4, 24);
        this.tabSorrow.Name = "tabSorrow";
        this.tabSorrow.Size = new System.Drawing.Size(918, 172);
        this.tabSorrow.TabIndex = 0;
        this.tabSorrow.Text = "Tab of Sorrow";
        //
        // rtbNotes
        //
        this.rtbNotes.BackColor = System.Drawing.Color.LightCyan;
        this.rtbNotes.Font = new System.Drawing.Font("Consolas", 10F);
        this.rtbNotes.ForeColor = System.Drawing.Color.DarkSlateGray;
        this.rtbNotes.Location = new System.Drawing.Point(6, 6);
        this.rtbNotes.Name = "rtbNotes";
        this.rtbNotes.Size = new System.Drawing.Size(904, 158);
        this.rtbNotes.TabIndex = 0;
        this.rtbNotes.Text = "Dear Diary,\n\nToday I mass-deleted node_modules and it was glorious.\nThe build broke immediately.\nI regret nothing.\n\n-- A Senior Developer, probably";
        //
        // tabDespair
        //
        this.tabDespair.BackColor = System.Drawing.Color.MistyRose;
        this.tabDespair.Controls.Add(this.dtpDoomsday);
        this.tabDespair.Controls.Add(this.lblDoomsday);
        this.tabDespair.Location = new System.Drawing.Point(4, 24);
        this.tabDespair.Name = "tabDespair";
        this.tabDespair.Size = new System.Drawing.Size(918, 172);
        this.tabDespair.TabIndex = 1;
        this.tabDespair.Text = "Tab of Despair";
        //
        // dtpDoomsday
        //
        this.dtpDoomsday.CalendarForeColor = System.Drawing.Color.Crimson;
        this.dtpDoomsday.Font = new System.Drawing.Font("Segoe UI", 10F);
        this.dtpDoomsday.Format = System.Windows.Forms.DateTimePickerFormat.Long;
        this.dtpDoomsday.Location = new System.Drawing.Point(6, 32);
        this.dtpDoomsday.Name = "dtpDoomsday";
        this.dtpDoomsday.Size = new System.Drawing.Size(300, 25);
        this.dtpDoomsday.TabIndex = 0;
        //
        // lblDoomsday
        //
        this.lblDoomsday.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Italic);
        this.lblDoomsday.ForeColor = System.Drawing.Color.DarkRed;
        this.lblDoomsday.Location = new System.Drawing.Point(6, 8);
        this.lblDoomsday.Name = "lblDoomsday";
        this.lblDoomsday.Size = new System.Drawing.Size(300, 20);
        this.lblDoomsday.TabIndex = 1;
        this.lblDoomsday.Text = "Select your doomsday:";
        //
        // trkVolume
        //
        this.trkVolume.LargeChange = 2;
        this.trkVolume.Location = new System.Drawing.Point(12, 470);
        this.trkVolume.Maximum = 10;
        this.trkVolume.Name = "trkVolume";
        this.trkVolume.Size = new System.Drawing.Size(450, 45);
        this.trkVolume.TabIndex = 5;
        this.trkVolume.TickFrequency = 1;
        this.trkVolume.Value = 7;
        //
        // prgDoom
        //
        this.prgDoom.BackColor = System.Drawing.Color.WhiteSmoke;
        this.prgDoom.ForeColor = System.Drawing.Color.OrangeRed;
        this.prgDoom.Location = new System.Drawing.Point(475, 478);
        this.prgDoom.Name = "prgDoom";
        this.prgDoom.Size = new System.Drawing.Size(463, 28);
        this.prgDoom.Style = System.Windows.Forms.ProgressBarStyle.Continuous;
        this.prgDoom.TabIndex = 6;
        this.prgDoom.Value = 42;
        //
        // lblWarning
        //
        this.lblWarning.AutoSize = true;
        this.lblWarning.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
        this.lblWarning.ForeColor = System.Drawing.Color.Red;
        this.lblWarning.Location = new System.Drawing.Point(12, 522);
        this.lblWarning.Name = "lblWarning";
        this.lblWarning.Size = new System.Drawing.Size(380, 19);
        this.lblWarning.TabIndex = 7;
        this.lblWarning.Text = "WARNING: Things will only get worse from here.";
        //
        // lblStatus
        //
        this.lblStatus.AutoSize = true;
        this.lblStatus.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Italic);
        this.lblStatus.ForeColor = System.Drawing.Color.Gray;
        this.lblStatus.Location = new System.Drawing.Point(12, 548);
        this.lblStatus.Name = "lblStatus";
        this.lblStatus.Size = new System.Drawing.Size(265, 15);
        this.lblStatus.TabIndex = 8;
        this.lblStatus.Text = "Status: Existential crisis in progress...";
        //
        // KitchenSinkForm
        //
        this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.BackColor = System.Drawing.Color.WhiteSmoke;
        this.ClientSize = new System.Drawing.Size(950, 580);
        this.Controls.Add(this.lblTitle);
        this.Controls.Add(this.grpDread);
        this.Controls.Add(this.pnlChaos);
        this.Controls.Add(this.grpVoid);
        this.Controls.Add(this.tabControl);
        this.Controls.Add(this.trkVolume);
        this.Controls.Add(this.prgDoom);
        this.Controls.Add(this.lblWarning);
        this.Controls.Add(this.lblStatus);
        this.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.Name = "KitchenSinkForm";
        this.Text = "The Kitchen Sink of Doom";
        this.grpDread.ResumeLayout(false);
        this.grpDread.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)(this.nudLevel)).EndInit();
        this.pnlChaos.ResumeLayout(false);
        this.pnlChaos.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)(this.picBox)).EndInit();
        this.grpVoid.ResumeLayout(false);
        this.tabControl.ResumeLayout(false);
        this.tabSorrow.ResumeLayout(false);
        this.tabDespair.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)(this.trkVolume)).EndInit();
        this.ResumeLayout(false);
        this.PerformLayout();
    }

    private System.Windows.Forms.Label lblTitle;
    private System.Windows.Forms.GroupBox grpDread;
    private System.Windows.Forms.Label lblFear;
    private System.Windows.Forms.TextBox txtFear;
    private System.Windows.Forms.CheckBox chkAccept;
    private System.Windows.Forms.RadioButton radBad;
    private System.Windows.Forms.RadioButton radWorse;
    private System.Windows.Forms.NumericUpDown nudLevel;
    private System.Windows.Forms.Label lblLevel;
    private System.Windows.Forms.Panel pnlChaos;
    private System.Windows.Forms.Button btnPanic;
    private System.Windows.Forms.Button btnCalm;
    private System.Windows.Forms.LinkLabel lnkHelp;
    private System.Windows.Forms.PictureBox picBox;
    private System.Windows.Forms.GroupBox grpVoid;
    private System.Windows.Forms.ComboBox cboMood;
    private System.Windows.Forms.ListBox lstBugs;
    private System.Windows.Forms.CheckedListBox clbTasks;
    private System.Windows.Forms.TabControl tabControl;
    private System.Windows.Forms.TabPage tabSorrow;
    private System.Windows.Forms.RichTextBox rtbNotes;
    private System.Windows.Forms.TabPage tabDespair;
    private System.Windows.Forms.DateTimePicker dtpDoomsday;
    private System.Windows.Forms.Label lblDoomsday;
    private System.Windows.Forms.TrackBar trkVolume;
    private System.Windows.Forms.ProgressBar prgDoom;
    private System.Windows.Forms.Label lblWarning;
    private System.Windows.Forms.Label lblStatus;
}
