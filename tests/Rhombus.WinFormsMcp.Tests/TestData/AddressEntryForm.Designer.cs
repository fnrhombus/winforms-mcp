namespace WinFormsApp1;

partial class AddressEntryForm {
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing) {
        if (disposing && (components != null)) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent() {
        lblTitle = new Label();
        lblSecName = new Label();
        lblSecAddress = new Label();
        lblSecContact = new Label();
        lblFirstName = new Label();
        txtFirstName = new TextBox();
        lblLastName = new Label();
        txtLastName = new TextBox();
        lblAddress1 = new Label();
        txtAddress1 = new TextBox();
        lblAddress2 = new Label();
        txtAddress2 = new TextBox();
        lblCity = new Label();
        txtCity = new TextBox();
        lblState = new Label();
        cboState = new ComboBox();
        lblZip = new Label();
        txtZip = new TextBox();
        lblCountry = new Label();
        cboCountry = new ComboBox();
        lblPhone = new Label();
        txtPhone = new TextBox();
        lblEmail = new Label();
        txtEmail = new TextBox();
        btnSubmit = new Button();
        btnClear = new Button();
        lblNote = new Label();
        this.SuspendLayout();
        // 
        // lblTitle
        // 
        lblTitle.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
        lblTitle.Location = new Point(16, 14);
        lblTitle.Name = "lblTitle";
        lblTitle.Size = new Size(528, 30);
        lblTitle.TabIndex = 0;
        lblTitle.Text = "Address Entry Form";
        lblTitle.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // lblSecName
        // 
        lblSecName.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        lblSecName.ForeColor = Color.Gray;
        lblSecName.Location = new Point(16, 52);
        lblSecName.Name = "lblSecName";
        lblSecName.Size = new Size(528, 16);
        lblSecName.TabIndex = 1;
        lblSecName.Text = "— Name ——————————————————";
        // 
        // lblSecAddress
        // 
        lblSecAddress.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        lblSecAddress.ForeColor = Color.Gray;
        lblSecAddress.Location = new Point(16, 108);
        lblSecAddress.Name = "lblSecAddress";
        lblSecAddress.Size = new Size(528, 16);
        lblSecAddress.TabIndex = 6;
        lblSecAddress.Text = "— Address ————————————————";
        // 
        // lblSecContact
        // 
        lblSecContact.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        lblSecContact.ForeColor = Color.Gray;
        lblSecContact.Location = new Point(16, 260);
        lblSecContact.Name = "lblSecContact";
        lblSecContact.Size = new Size(528, 16);
        lblSecContact.TabIndex = 19;
        lblSecContact.Text = "— Contact ————————————————";
        // 
        // lblFirstName
        // 
        lblFirstName.Location = new Point(16, 74);
        lblFirstName.Name = "lblFirstName";
        lblFirstName.Size = new Size(80, 20);
        lblFirstName.TabIndex = 2;
        lblFirstName.Text = "First Name *";
        lblFirstName.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // txtFirstName
        // 
        txtFirstName.Location = new Point(100, 72);
        txtFirstName.Name = "txtFirstName";
        txtFirstName.Size = new Size(180, 23);
        txtFirstName.TabIndex = 3;
        // 
        // lblLastName
        // 
        lblLastName.Location = new Point(296, 74);
        lblLastName.Name = "lblLastName";
        lblLastName.Size = new Size(76, 20);
        lblLastName.TabIndex = 4;
        lblLastName.Text = "Last Name *";
        lblLastName.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // txtLastName
        // 
        txtLastName.Location = new Point(376, 72);
        txtLastName.Name = "txtLastName";
        txtLastName.Size = new Size(168, 23);
        txtLastName.TabIndex = 5;
        // 
        // lblAddress1
        // 
        lblAddress1.Location = new Point(16, 130);
        lblAddress1.Name = "lblAddress1";
        lblAddress1.Size = new Size(102, 20);
        lblAddress1.TabIndex = 7;
        lblAddress1.Text = "Street Address *";
        lblAddress1.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // txtAddress1
        // 
        txtAddress1.Location = new Point(122, 128);
        txtAddress1.Name = "txtAddress1";
        txtAddress1.Size = new Size(422, 23);
        txtAddress1.TabIndex = 8;
        // 
        // lblAddress2
        // 
        lblAddress2.Location = new Point(16, 162);
        lblAddress2.Name = "lblAddress2";
        lblAddress2.Size = new Size(102, 20);
        lblAddress2.TabIndex = 9;
        lblAddress2.Text = "Apt / Suite";
        lblAddress2.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // txtAddress2
        // 
        txtAddress2.Location = new Point(122, 160);
        txtAddress2.Name = "txtAddress2";
        txtAddress2.Size = new Size(422, 23);
        txtAddress2.TabIndex = 10;
        // 
        // lblCity
        // 
        lblCity.Location = new Point(16, 194);
        lblCity.Name = "lblCity";
        lblCity.Size = new Size(102, 20);
        lblCity.TabIndex = 11;
        lblCity.Text = "City *";
        lblCity.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // txtCity
        // 
        txtCity.Location = new Point(122, 192);
        txtCity.Name = "txtCity";
        txtCity.Size = new Size(160, 23);
        txtCity.TabIndex = 12;
        // 
        // lblState
        // 
        lblState.Location = new Point(292, 194);
        lblState.Name = "lblState";
        lblState.Size = new Size(46, 20);
        lblState.TabIndex = 13;
        lblState.Text = "State *";
        lblState.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // cboState
        // 
        cboState.DropDownStyle = ComboBoxStyle.DropDownList;
        cboState.Items.AddRange(new object[] { "", "AL", "AK", "AZ", "AR", "CA", "CO", "CT", "DE", "FL", "GA", "HI", "ID", "IL", "IN", "IA", "KS", "KY", "LA", "ME", "MD", "MA", "MI", "MN", "MS", "MO", "MT", "NE", "NV", "NH", "NJ", "NM", "NY", "NC", "ND", "OH", "OK", "OR", "PA", "RI", "SC", "SD", "TN", "TX", "UT", "VT", "VA", "WA", "WV", "WI", "WY", "DC" });
        cboState.Location = new Point(342, 192);
        cboState.Name = "cboState";
        cboState.Size = new Size(80, 23);
        cboState.TabIndex = 14;
        // 
        // lblZip
        // 
        lblZip.Location = new Point(16, 226);
        lblZip.Name = "lblZip";
        lblZip.Size = new Size(102, 20);
        lblZip.TabIndex = 15;
        lblZip.Text = "ZIP Code *";
        lblZip.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // txtZip
        // 
        txtZip.Location = new Point(122, 224);
        txtZip.Name = "txtZip";
        txtZip.Size = new Size(100, 23);
        txtZip.TabIndex = 16;
        // 
        // lblCountry
        // 
        lblCountry.Location = new Point(234, 226);
        lblCountry.Name = "lblCountry";
        lblCountry.Size = new Size(54, 20);
        lblCountry.TabIndex = 17;
        lblCountry.Text = "Country";
        lblCountry.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // cboCountry
        // 
        cboCountry.DropDownStyle = ComboBoxStyle.DropDownList;
        cboCountry.Items.AddRange(new object[] { "United States", "Canada", "Mexico", "Other" });
        cboCountry.Location = new Point(292, 224);
        cboCountry.Name = "cboCountry";
        cboCountry.Size = new Size(152, 23);
        cboCountry.TabIndex = 18;
        // 
        // lblPhone
        // 
        lblPhone.Location = new Point(16, 282);
        lblPhone.Name = "lblPhone";
        lblPhone.Size = new Size(80, 20);
        lblPhone.TabIndex = 20;
        lblPhone.Text = "Phone";
        lblPhone.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // txtPhone
        // 
        txtPhone.Location = new Point(100, 280);
        txtPhone.Name = "txtPhone";
        txtPhone.Size = new Size(160, 23);
        txtPhone.TabIndex = 21;
        // 
        // lblEmail
        // 
        lblEmail.Location = new Point(278, 282);
        lblEmail.Name = "lblEmail";
        lblEmail.Size = new Size(42, 20);
        lblEmail.TabIndex = 22;
        lblEmail.Text = "Email";
        lblEmail.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // txtEmail
        // 
        txtEmail.Location = new Point(324, 280);
        txtEmail.Name = "txtEmail";
        txtEmail.Size = new Size(220, 23);
        txtEmail.TabIndex = 23;
        // 
        // btnSubmit
        // 
        btnSubmit.Location = new Point(315, 337);
        btnSubmit.Name = "btnSubmit";
        btnSubmit.Size = new Size(96, 30);
        btnSubmit.TabIndex = 25;
        btnSubmit.Text = "Submitttt";
        btnSubmit.Click += this.btnSubmit_Click;
        // 
        // btnClear
        // 
        btnClear.Location = new Point(452, 318);
        btnClear.Name = "btnClear";
        btnClear.Size = new Size(92, 30);
        btnClear.TabIndex = 26;
        btnClear.Text = "Clear";
        btnClear.Click += this.btnClear_Click;
        // 
        // lblNote
        // 
        lblNote.Font = new Font("Segoe UI", 8F, FontStyle.Italic);
        lblNote.ForeColor = Color.Gray;
        lblNote.Location = new Point(16, 320);
        lblNote.Name = "lblNote";
        lblNote.Size = new Size(200, 18);
        lblNote.TabIndex = 24;
        lblNote.Text = "* Required fields";
        // 
        // AddressEntryForm
        // 
        this.ClientSize = new Size(560, 470);
        this.Controls.Add(lblTitle);
        this.Controls.Add(lblSecName);
        this.Controls.Add(lblFirstName);
        this.Controls.Add(txtFirstName);
        this.Controls.Add(lblLastName);
        this.Controls.Add(txtLastName);
        this.Controls.Add(lblSecAddress);
        this.Controls.Add(lblAddress1);
        this.Controls.Add(txtAddress1);
        this.Controls.Add(lblAddress2);
        this.Controls.Add(txtAddress2);
        this.Controls.Add(lblCity);
        this.Controls.Add(txtCity);
        this.Controls.Add(lblState);
        this.Controls.Add(cboState);
        this.Controls.Add(lblZip);
        this.Controls.Add(txtZip);
        this.Controls.Add(lblCountry);
        this.Controls.Add(cboCountry);
        this.Controls.Add(lblSecContact);
        this.Controls.Add(lblPhone);
        this.Controls.Add(txtPhone);
        this.Controls.Add(lblEmail);
        this.Controls.Add(txtEmail);
        this.Controls.Add(lblNote);
        this.Controls.Add(btnSubmit);
        this.Controls.Add(btnClear);
        this.Font = new Font("Segoe UI", 9F);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.Name = "AddressEntryForm";
        this.StartPosition = FormStartPosition.CenterScreen;
        this.Text = "Address Entry Form";
        this.Load += this.AddressEntryForm_Load;
        this.ResumeLayout(false);
        this.PerformLayout();
    }

    private System.Windows.Forms.Label    lblTitle;
    private System.Windows.Forms.Label    lblSecName, lblSecAddress, lblSecContact;
    private System.Windows.Forms.Label    lblFirstName, lblLastName;
    private System.Windows.Forms.TextBox  txtFirstName, txtLastName;
    private System.Windows.Forms.Label    lblAddress1, lblAddress2, lblCity, lblState, lblZip, lblCountry;
    private System.Windows.Forms.TextBox  txtAddress1, txtAddress2, txtCity, txtZip;
    private System.Windows.Forms.ComboBox cboState, cboCountry;
    private System.Windows.Forms.Label    lblPhone, lblEmail;
    private System.Windows.Forms.TextBox  txtPhone, txtEmail;
    private System.Windows.Forms.Button   btnSubmit, btnClear;
    private Label lblNote;
}
