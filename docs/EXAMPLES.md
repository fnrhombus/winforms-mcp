# Rhombus.WinFormsMcp -- Example Workflows

Real-world examples of using the WinForms MCP server through Claude Code conversations. These show what you say to Claude, and what Claude does behind the scenes using the MCP tools.

## Table of Contents

1. [Designing a Form from Scratch](#1-designing-a-form-from-scratch)
2. [Previewing an Existing Form](#2-previewing-an-existing-form)
3. [Iterating on Form Design](#3-iterating-on-form-design)
4. [Testing a Running App with FlaUI](#4-testing-a-running-app-with-flaui)
5. [Validating UI Changes](#5-validating-ui-changes)

---

## 1. Designing a Form from Scratch

You want Claude to create a login form. Claude writes the standard Visual Studio three-file convention, then previews it instantly without building.

```
> User: Create a login form with username, password, and a submit button.
>       Put it in src/MyApp/Forms/LoginForm.
```

Claude creates three files following Visual Studio conventions:

**LoginForm.cs** -- the code-behind with constructor and event handlers:

```csharp
namespace MyApp.Forms
{
    public partial class LoginForm : Form
    {
        public LoginForm()
        {
            InitializeComponent();
        }

        private void btnSubmit_Click(object sender, EventArgs e)
        {
            // TODO: authentication logic
        }
    }
}
```

**LoginForm.Designer.cs** -- all layout and control creation:

```csharp
namespace MyApp.Forms
{
    partial class LoginForm
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
            this.lblUsername = new System.Windows.Forms.Label();
            this.lblPassword = new System.Windows.Forms.Label();
            this.txtUsername = new System.Windows.Forms.TextBox();
            this.txtPassword = new System.Windows.Forms.TextBox();
            this.btnSubmit = new System.Windows.Forms.Button();
            this.SuspendLayout();
            //
            // lblUsername
            //
            this.lblUsername.AutoSize = true;
            this.lblUsername.Location = new System.Drawing.Point(30, 30);
            this.lblUsername.Name = "lblUsername";
            this.lblUsername.Size = new System.Drawing.Size(63, 15);
            this.lblUsername.Text = "Username:";
            //
            // txtUsername
            //
            this.txtUsername.Location = new System.Drawing.Point(130, 27);
            this.txtUsername.Name = "txtUsername";
            this.txtUsername.Size = new System.Drawing.Size(200, 23);
            //
            // lblPassword
            //
            this.lblPassword.AutoSize = true;
            this.lblPassword.Location = new System.Drawing.Point(30, 70);
            this.lblPassword.Name = "lblPassword";
            this.lblPassword.Size = new System.Drawing.Size(60, 15);
            this.lblPassword.Text = "Password:";
            //
            // txtPassword
            //
            this.txtPassword.Location = new System.Drawing.Point(130, 67);
            this.txtPassword.Name = "txtPassword";
            this.txtPassword.Size = new System.Drawing.Size(200, 23);
            //
            // btnSubmit
            //
            this.btnSubmit.Location = new System.Drawing.Point(130, 110);
            this.btnSubmit.Name = "btnSubmit";
            this.btnSubmit.Size = new System.Drawing.Size(100, 30);
            this.btnSubmit.Text = "Log In";
            this.btnSubmit.Click += new System.EventHandler(this.btnSubmit_Click);
            //
            // LoginForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(370, 170);
            this.Controls.Add(this.btnSubmit);
            this.Controls.Add(this.txtPassword);
            this.Controls.Add(this.lblPassword);
            this.Controls.Add(this.txtUsername);
            this.Controls.Add(this.lblUsername);
            this.Name = "LoginForm";
            this.Text = "Login";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.Label lblUsername;
        private System.Windows.Forms.Label lblPassword;
        private System.Windows.Forms.TextBox txtUsername;
        private System.Windows.Forms.TextBox txtPassword;
        private System.Windows.Forms.Button btnSubmit;
    }
}
```

Claude then previews the form immediately -- no build required:

```
Claude calls render_form:
  designerFilePath: "C:\project\src\MyApp\Forms\LoginForm.Designer.cs"
  outputPath: "C:\temp\login-preview.png"
```

Claude sees the rendered PNG and responds:

```
Here's the login form. It has a Username field, Password field, and a Log In
button. The layout is clean with left-aligned labels and consistent spacing.
Let me know if you want any changes.
```

![Login form preview](data:image/png;base64,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)

---

## 2. Previewing an Existing Form

You point Claude at an existing form and ask for a preview. Claude picks the right renderer based on what controls the form uses.

### Standard controls only -- use `render_form`

```
> User: Show me what Forms/SettingsForm.Designer.cs looks like.
```

Claude reads the designer file and renders a preview:

```
Claude calls render_form:
  designerFilePath: "C:\project\Forms\SettingsForm.Designer.cs"

Completed in ~150ms (warm) / ~1s (cold start), returns base64 PNG
```

The `render_form` tool automatically detects the project's target framework from its `.csproj` and renders in a matching out-of-process host — no configuration needed. It handles standard controls, custom UserControls, and third-party controls alike.

---

## 3. Iterating on Form Design

The fast edit-render loop. You describe changes, Claude edits the designer file and re-renders until you're happy.

```
> User: Make the password field use a password mask.
```

Claude edits `LoginForm.Designer.cs`, adding one property:

```csharp
this.txtPassword.UseSystemPasswordChar = true;
```

```
Claude calls render_form:
  designerFilePath: "C:\project\src\MyApp\Forms\LoginForm.Designer.cs"
  outputPath: "C:\temp\login-preview-2.png"
```

```
Done. The password field now masks input with dots. Here's the updated preview.
```

![Login form with password mask](data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAZMAAAEACAYAAAB27puMAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAABAWSURBVHhe7d1bjyTlfcDh+U4rzQfgGnwTKRd74fRVvsZeJDCYKBytSCvZ8RorGDOgmMDaEgHMsGfYmT0fvAdgl+MCsTexxAUCVertruqprnlrunreme6u7ucvPZruOnujfX90D3ZWHj58mAGwvMLEtk9CTACWnJgAkExMAEgmJgAkExMAkokJAMnGxaTX60W3V4kJwJLbLSYhJKXY/pKYACy5pphUQzIuKGICsORiMakHpP6+TkwAltxuMRm3rSQmAEuuKSb1bbttFxOAJReLyaTEBGDJiQkAyWYek5WVlRGPPfYYAB0zNzEJD/OTn/wke+bZ57LzF29m97/4KwAdMRcxCSH56U//ITu3eS370+ZtADomzObm5p71e1APxCRCTMInkn5Itu4A0EEzj0n4VPLEE09l7+UPA0A3zUVMwu9INi7cBaCj5iImD779W7Zx8SMAOmpuYvJ+/jAAdNPcxOTEpY8B6Ki5icnJy58A0FFiAiyMt05fz37+q+PZPz398lSFe4Z7L/NzzE1MTl25B5DkX/7tP7P/Pnk5u3T786kK9wz3XubnmJuYnL56f0++++67sWLnAYsn/BPyhT9/lr3z4a2pCvcM917m51iImDzyyCONxASWxz8/87vs0q3Ps3fP356qcM9w7y49xzvnb1VeF9s/jOjvy48dvi9el+cVr+cmJmeufbonbWISOw9YPP3F886X2Z+27k5VuGe496TP8fjTv8z+7u//caxwXOz8usbn2LxT/G9ohZ9VeQjyn48//YvR+/1rfr98X/g5sj1/jnB8OPfd85Vr5K/nJiZnr3+2J21iEjvv7PXz2e+OvpS9fj62bw/Ov5f9ej+vB0zs8Wdfya7ki+f7Fz+eqnDPcO9JnyMs0D/88MNY4bjY+XVNz/FeHpptHxXuZhvFz3D96oT3Tzz979Hto9faFiYWibZCD/YlJudufD5WCENMLCKlsD92rXM3iphsxvYBXdRfPO9+lZ3IF9ZpuprfM9x75DnyRfzExU92FRbnWDzqwnGx8+uuFjGpPsfVj77KNvJn3LgQ/pvqxc++PECFJ54ZhOPHH38cir0Px/WvcbFyfv5+rv5Lix/e/GKsWDjaxCR2rQ9vbmavHP1t9l9bsX1AF/UXz7vjF/H9diVfsMO9R54j33by0r1dVWMSXtdV98XOryujtvM58uesGWy7N3y/9uyv+vf5/vvvdwjbw/7t87fPK689NzE5f+vLsfYak9i1zt/a6sfkjQuRfRc2shePHs2OFl45Fd/34lsblWtUr1e8fuuN4TVefOvW9jWAA/H4c+vZtY+/zk5d+XRXTz57bLhg7yYcFzu/Ltwz3HvS5wj3ePDgQV943XZfk8bnuBz+VeH7fSev5OEJ/+pwvq3/M2wP+y/fz//zDoJS3re8d9g+PK//c3Ct7Wven5+YbN7+aqy9xiR2rc3bFwYL/sXY9hCQ4v3FEI/yuNF9J9/6bR6K6r7R447+/sLgGqfezI97M3t7eA/gIEyyiLeZSRfxvTzH/fv3+8Lruuq+2Pl1jc/RIiYhGNV7VoXt4ZNJJ2KydefBWE0xGSd2ra07F7L1fPF/81Jt+6X383jkC39l29u/P5qtnyn2/cf72anhvuo1ml7H3gMHISye1z9pt4jfuXNnrNaLeH7PcO/qc4Rtp/N9u5nkOWLn15UxGXmOfNvpq4N/u+v01c8qBu/P5D+ffG7wSa1+z/r7n+XHnbmWnxv+bbFwzXCN/nU/nZ+YXLj79VghDLGY1LeV22PX2DZY4I9frm2/HGJyPHu3su3d10NMin15TM4M91Wv0fQ69h44CE8Ui/ipfHHbTVgYb9y4MVY4LnZ+XQhYuHf1OcK20cV7p0meI3Z+XfjPXn2OMiZnr3+encmFn1WDbTufI7z/2fO/jm6PXSeYm5hc/OibsSaNSewa2y5mrx59OTt+Jbb9aPbq2eL9lRODuET2nX375ezo8BrV69Wv3XQvYD+FhfTmvW+ji11VuVCOE46LnV8X7hnuPa/Pce7ml7kvsnM3vsg+yH8G5258mf8ceKr2HOF9OOepF2rbX3hxcF4uXC/8/CC/Zrju3MTk0sffjhUCEdMUk9g1tl3qh6H8BXnwm3c+Guy7eiL7zXD7y9kfrlbOO3u8cvyJfiQG+8P1Yq9j74GDsPb8q8Ui/sVUhXuGe8/rc3z4568ivuz//KB4X/4s1d9XDffd3N42NzG5/Mn/7Ek9LFWx4/fVtZN5dP6QvRfbB0xdWDxvffqXfHF7MFXhnuHe8/oc5289qPi6UN32INuMbBsed7t8/XV+XHhf/MyV7+cmJlfu/aVz3ns9/4Ty0qnsXGQfMH1PvvBadvvzh9nWnW+mKtwz3Hten2MzLP63vymE16X8/Z2vs61ye36NvmL7jmN3nL9tbmJy9f5fO+Be9seXyq+/gj9mG9HjgFl4/pfHs9MX7mYff/W3qQr3DPde5ueYm5hcu/8QIMm5a19kz/3ieP+fyKcp3DPce5mfY25icv3T/wWgo+YnJp/lDwRAJ81NTG589n8AdNTMY/Loo4/uGpPYOQDMFzEBIJmYAJBMTABIJiYAJBMTAJKJCQDJxASAZGICQDIxASCZmACQTEwASCYmACQTEwCSiQkAycQEgGRiAkAyMQEgmZgAkExMAEgmJgAkExMAkokJAMnEBIBkYgJAMjEBIJmYAJBMTOiE9dfeABrE/s5Mm5jQCeEvjDFm54hJQUxoQ0yMiY+YFMSENsTEmPiISUFMaENMjImPmBTEhDbExJj4iEmhGzFZz3orq9na1rhtHBQxMSY+YlIQE9oQE2PiIyYFMaENMTEmPmJSWIyYbGVrqyvZyspAb704ZmstWy22raz0svXKeb3earGtuM5ab3j+6tpWcY98Ee1tX3elt15sL85Z375+uOfWWrjmzmtEn2M9v9/qWrZVHjPnxMSY+IhJYSFiEhbm4UIf2R/e58cMFviwvbrYD94Pzw/XGoanqnq94pwyBv1zKtccuUbDc4RjxMSYzo+YFBYiJsU/+Td/Gij0g1G/1pj3RSgGyu0TXKPxOcpju0FMjImPmBS6EZPwNVZ1sc71F+nRTxDl10z9r7ki+wcmDUF5jeoz7PUa3SUmxsRHTArdiEnxu4vK10L995F/wg9BiX+dVZogBNWvovpRKI+b4Br915Hn8DWXMQsxYlLoSkzqv2QfWYhHvoqqfAroB6ByzsRfc1XuudrLenv5ZNL0HGJizEKMmBS6ExNmSUyMiY+YFMSENsTEmPiISUFMaENMjImPmBTEhDbExJj4iElBTGhDTIyJj5gUxIQ2xMSY+IhJQUxoQ0yMiY+YFMSENsJfGCAu9ndm2sQEgGRiAkAyMQEgmZgAkExMAEgmJgAkExMAkokJAMnEBIBkYgJAMjEBIJmYAJBMTABIJiYAJBMTAJKJCQDJxASAZGJCJ8T+v8sBA7G/M9MmJnRC+AtjjNk5YlIQE9oQE2PiIyYFMaENMTEmPmJSEBPaEBNj4iMmBTGhDTExJj5iUuhGTNaz3spKtjK0mq1txY6bR+HZu/S8cWJiTHzEpNCdmGwvyFtrq9nK6lq2teO4eSQmxizyiEmhizHp1gItJsYs8ohJYRFist6rfAXWWy+O2crWVre399bj28K5g307r/tway1bLa8XXhfnbV9v+5xeL/+0tNLL1mvHrq6tbV9zvdehT1SjxMSY+IhJYbG+5qocFxbuYVgK47blr1dXV/MAbPXfh/sMXofrVgLSj0X5PIN95Tn1Y/vPKibGLOyISaGbv4AvPgGUwiI93Fcs3MWng+1FfpdtxQK/nn+6WFsv34dPMdVrjd5z+xNNJWC16w2Ore3vKDExJj5iUujm11wVIwt9JQDF/sEng+rXUvVt5Tn5PYYRya9XjYKYiIkxDSMmhc7HpPrVUX/R33nc9tdV8W3917Wvt3q9XuWccP9KkEbiUn+20WMH4Sr2+5rLmIUbMSl0PibVX6rnnyh65SeTka++ioU/ti2oRygWpf628tzqvsizVe7jF/DGLPaISaEbMWHWxMSY+IhJQUxoQ0yMiY+YFMSENsTEmPiISUFMaENMjImPmBTEhDbExJj4iElBTGhDTIyJj5gUxIQ2xMSY+IhJQUxoI/yFAeJif2emTUwASCYmACQTEwCSiQkAycQEgGRiAkAyMQEgmZgAkExMAEgmJgAkExMAkokJAMnEBIBkYgJAMjEBIJmYAJBMTABIJiYAJBMTAJKJCQDJxASAZGJCZ7z22mvMgdj/bUBM6IywkN27d48ZEhOaiAmdISazJyY0ERM6Q0xmT0xoIiZ0hpjMnpjQREzoDDGZPTGhiZjQGWIye2JCEzGhM8Rk9sSEJmJCZ4jJ7IkJTcSEzph9TI5lh1cOZUc2YvtShWsfzo5F980PMaGJmNAZYjJ7YkITMaEzxGT2xIQmYkJnzHVMNo5kh1ZWspXC4WPxfYeOHGm4RjUmxX2OHB5e79CRjdrxsyEmNBETOmN+YxK2VwLSj0d53Oi+jSOH8ji0iUkekcPHBvuOhajMx6cWMaGJmNAZcxuTfjxGF/tjh4uAhH2HjmQbw30N19gRk+oxTedMn5jQREzoDDEp38+OmNBETOiMuY1Jf3v9a65qGPbyNZeY0C1iQmfMR0y2f8k+8ovxfkDK7bWFv/87j/L4CX4BLyZ0iJjQGbOPyT6IfCXWJWJCEzGhMxYhJuF3KSsjv0PpFjGhiZjQGd2MyUZ25FD59VfQ3U8lgZjQREzojEX4ZNJ1YkITMaEzxGT2xIQmYkJniMnsiQlNxITOEJPZExOaiAmdISazJyY0ERM6Q0xmT0xoIiZ0RljImL3Y/21ATABIJiYAJBMTAJKJCQDJxASAZGICQDIxASCZmACQTEwASCYmACQTEwCSiQkAycQEgGRiAkAyMQEgWZjY9kmICcCSExMAkokJAMnEBIBkYgJAMjEBIJmYAJBMTABIJiYAJBMTAJKJCQDJxASAZGICQDIxASCZmACQTEwASCYmACQTEwCSiQkAycQEgGRiAkAyMQEgmZgAkExMAEgmJgAkExMAkokJAMnEBIBkYgJAslhMer3ejm27bRcTgCXXFJN6OGLbSmICsOR2i0kZj/r7OjEBWHKxmATVgOwWkkBMAJZcU0yCNiEJxARgye0Wk2BcSAIxAVhy42LShpgALDkxASCZmACQTEwASLYvMelfxRhjzNJOLA6TSvpkUtrc3ASgJrZeLioxATggsfVyUYkJwAGJrZeLSkwADkhsvVxUY2PS5n+XJfaHCLDsYuvltLRZu/fTrjFp+zCxP0SAZRdbL6ep7Rq+HxpjMslDxP4QAZZdbL2ctknW8hTRmEx689gfIsCyi62XszDpmr4XPpkAHJDYejltk6zlKfzOBOCAxNbLaWq7hu+HXWMStHmY2B8iwLKLrZfT0mbt3k9jY9JG7A8RYNnF1stFJSYAByS2Xi4qMQE4ILH1clGJCcABia2Xi2pfYgLAMnuY/T+8cmz46wAn4wAAAABJRU5ErkJggg==)

```
> User: Add a "Remember me" checkbox below the password field, and make the
>       submit button wider.
```

Claude edits the designer file -- adds a `CheckBox` control, adjusts button width, shifts the button down to make room:

```
Claude calls render_form:
  designerFilePath: "C:\project\src\MyApp\Forms\LoginForm.Designer.cs"
  outputPath: "C:\temp\login-preview-3.png"
```

```
Added a "Remember me" checkbox at (130, 100) and widened the Log In button
to 200px. The button moved down to (130, 135) to make room for the checkbox.
The form height increased to 200 to fit everything. Here's the result.
```

![Login form with checkbox and wider button](data:image/png;base64,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)

```
> User: Looks good, but center the button horizontally.
```

Claude recalculates: form client width is 370, button width is 200, so `(370 - 200) / 2 = 85`. Edits the button's X position to 85.

```
Claude calls render_form:
  designerFilePath: "C:\project\src\MyApp\Forms\LoginForm.Designer.cs"
  outputPath: "C:\temp\login-preview-4.png"
```

```
Centered. The button is now at X=85 which puts it in the horizontal center
of the 370px-wide form.
```

![Login form with centered button](data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAZMAAAEeCAYAAABPMvhnAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAABI5SURBVHhe7d1bjyNXgcDx/k4j9QfgOfCy0j7kgfXTfo152E06sNqQAFppJFiGoIVAEy0sDEhZEtJJJsmQac89E+aSZCbXCSzMLhIPCFRbxz7lLlcf2+U+tsd2/Y70U7tup4qR+vzH9gA7Dx8+LADorjBS++chJgAdJyYAZBMTALKJCQDZxASAbGICQLZZMen1esn9dWIC0HHTYhJCUkkdr4gJQMdNikk9JLOCIiYAHZeKSTMgze0mMQHouGkxmbWvIiYAHTcpJs190/aLCUDHpWIyLzEB6DgxASDbI4/Jzs7OmC9+8YsAbJi1iUl4mC996UvF1555trh4+d3i/id/BGBDrEVMQki+/OV/KC4c3ih+fXgbgA0TxuHh4YkNetAMxDxCTMI7kkFI+ncA2ECPPCbhXcmTT361eKV8GAA201rEJHxHcnDpLgAbai1i8uD3fyoOLr8HwIZam5i8Wj4MAJtpbWLy2pX3AdhQaxOT169+AMCGEhNga7z4xjvFN79zrvinp59fqXDPcO8uP8faxOT8tXsAWf7l3/6z+O/XrxZXbn+8UuGe4d5dfo61ickb1++fyJ///OeZUtcB2yf8DfnSbz8qXnr71kqFe4Z7d/k5tiImX/jCFyYSE+iOf/7aD4srtz4uXr54e6XCPcO9N+k5Xrp4q/Y67n87YXCsPHe0HV9X18XXaxOTN298eCJtYpK6Dtg+g8XzzqfFr/t3VyrcM9x73ud44ulvF3/39/84UzgvdX3TxOc4vBP/N7TCz7oyBOXPJ57+1vj9/rW8X3ks/BzbXz5HOD9c+/LF2hzl67WJyVvvfHQibWKSuu6tdy4WPzzz/eKnF1PHTuDiK8V3FzkfMLcnnvlRca1cPF+9/P5KhXuGe8/7HGGB/utf/zpTOC91fdOk53ilDM2R96K7xUH8Geavj7D95NP/ntw/PteRMFKRaCv0YCExuXDz45lCGFJSEamE46m5LtyMMTlMHQM20WDxvPtZ8Vq5sK7S9fKe4d5jz1Eu4q9d/mCqsDin4tEUzktd33Q9xqT+HNff+6w4KJ/x4FL4b6rHnwNlgKInvzYMx9/+9reR1HY4bzDH5dr15fZa/ZcW3373k5lS4WgTk9Rcb797WPzozA+K/+qnjgGbaLB43p29iC/atXLBDvcee45y3+tX7k1Vj0l43VQ/lrq+qYra8econ7NhuO/eaHvvme8M7vOXv/zlmLA/HD+6/ui6au61icnFW5/OdNKYpOa6eKs/iMnPLiWOXToonjtzpjgT/eh8+thzLx7U5qjPF1+/+LPRHM+9eOtoDmApnnh2v7jx/ufF+WsfTvXUM2dHC/Y04bzU9U3hnuHe8z5HuMeDBw8Gwuu2xyaZ+BxXwz8Vvj/w+rUyPOGfDpf7Bj/D/nD86v3yP+8wKNV9q3uH/aPrBj+Hcx3NeX99YnJ4+7OZThqT1FyHty8NF/zLqf0hIHH7cohHdd74sddf/EEZivqx8fPO/OTScI7zPy/P+3nxq9E9gGWYZxFvM+ZdxE/yHPfv3x8Ir5vqx1LXN018jhYxCcGo37Mu7A/vTDYiJv07D2aaFJNZUnP171wq9svF/+dXGvuvvFrGo1z4a/t+9ZMzxf6b8dh/vFqcHx2rzzHpdWobWIaweL7zQbtF/M6dOzO1XsTLe4Z7158j7HujPDbNPM+Rur6pisnYc5T73rg+/Nddb1z/qGa4/Wb586lnh+/Umvdsbn+lPO/NG+W14V+LhTnDHIN5P1yfmFy6+/lMIQypmDT3VftTcxwZLvDnrjb2Xw0xOVe8XNv38k9DTOKxMiZvjo7V55j0OrUNLMOTcRE/Xy5u04SF8ebNmzOF81LXN4WAhXvXnyPsG1+8j5vnOVLXN4X/7PXnqGLy1jsfF2+Wws+64b7jzxG2v/L17yb3p+YJ1iYml9/73UzzxiQ1x5HLxY/PPF+cu5baf6b48Vtx+9prw7gkjr31q+eLM6M56vM15550L2CRwkL67r3fJxe7umqhnCWcl7q+Kdwz3Htdn+PCu5+WPiku3Pyk+E35M7hw89Py59BXG88RtsM1X/1GY/83nhteVwrzhZ+/KecM865NTK68//uZQiBSJsUkNceRK4MwVF+QB9976b3hseuvFd8b7X+++MX12nVvnaud/9ogEsPjYb7U69Q2sAx7X/9xXMQ/Walwz3DvdX2Ot3/7WcKng5+/idvVz0pzu2507N2jfWsTk6sf/M+JNMNSlzp/oW68XkbnF8UrqWPAyoXF89aHfygXtwcrFe4Z7r2uz3Hx1oOaz6P6vgfFYWLf6Lzb1evPy/PCdvxZqrbXJibX7v1h47zy0/IdyvfPFxcSx4DVe+obLxS3P35Y9O/8bqXCPcO91/U5DsPif/t3UXhdKbfvfF70q/3lHANx/7Fzj11/ZG1icv3+HzfAveKX368+/gp+WRwkzwMeha9/+1zxxqW7xfuf/Wmlwj3Dvbv8HGsTkxv3HwJkuXDjk+LZb50b/I18lcI9w727/BxrE5N3PvxfADbU+sTko/KBANhIaxOTmx/9HwAb6pHH5LHHHpsak9Q1AKwXMQEgm5gAkE1MAMgmJgBkExMAsokJANnEBIBsYgJANjEBIJuYAJBNTADIJiYAZBMTALKJCQDZxASAbGICQDYxASCbmACQTUwAyCYmAGQTEwCyiQkA2cQEgGxiAkA2MQEgm5gAkE1M2Aj7L/wMmCD1O7NqYsJGCL8whmEcH2ISiQltiIlhpIeYRGJCG2JiGOkhJpGY0IaYGEZ6iEkkJrQhJoaRHmISbUZM9ovezm6x15+1j2URE8NIDzGJxIQ2xMQw0kNMIjGhDTExjPQQk2g7YtIv9nZ3ip2dod5+PKe/V+zGfTs7vWK/dl2vtxv3xXn2eqPrd/f68R7lIto7mnentx/3x2v2j+YP9+zvhTmPz5F8jv3yfrt7Rb86Z82JiWGkh5hEWxGTsDCPFvrE8bBdnjNc4MP++mI/3B5dH+YahaeuPl+8porB4JranGNzTHiOcI6YGMbGDzGJtiIm8W/+k98NRINgNOeasR1DMVTtn2OOic9RnbsZxMQw0kNMos2ISfgYq75YlwaL9Pg7iOpjpsHHXInjQ/OGoJqj/gwnnWNziYlhpIeYRJsRk/jdRe1jocF24m/4ISjpj7Mqc4Sg/lHUIArVeXPMMXideA4fcxnGVgwxiTYlJs0v2ccW4rGPomrvAgYBqF0z98dctXvu9oreSd6ZTHoOMTGMrRhiEm1OTHiUxMQw0kNMIjGhDTExjPQQk0hMaENMDCM9xCQSE9oQE8NIDzGJxIQ2xMQw0kNMIjGhDTExjPQQk0hMaENMDCM9xCQSE9oIvzBAWup3ZtXEBIBsYgJANjEBIJuYAJBNTADIJiYAZBMTALKJCQDZxASAbGICQDYxASCbmACQTUwAyCYmAGQTEwCyiQkA2cQEgGxiwkZI/b/LAUOp35lVExM2QviFMQzj+BCTSExoQ0wMIz3EJBIT2hATw0gPMYnEhDbExDDSQ0wiMaENMTGM9BCTaDNisl/0dnaKnZHdYq+fOm8dhWffpOdNExPDSA8xiTYnJkcLcn9vt9jZ3Sv6x85bR2JiGNs8xCTaxJhs1gItJoaxzUNMom2IyX6v9hFYbz+e0y/2do/29/bT+8K1w2PH533Y3yt2q/nC63jd0XxH1/R65bulnV6x3zh3d2/vaM793ga9oxonJoaRHmISbdfHXLXzwsI9Cks0a1/5end3twxAf7Ad7jN8HeatBWQQi+p5hseqa5rnDp5VTAxja4eYRJv5BXx8B1AJi/ToWFy447uDo0V+yr64wO+X7y729qvt8C6mPtf4PY/e0dQC1phveG7j+IYSE8NIDzGJNvNjrpqxhb4WgHh8+M6g/rFUc191TXmPUUTK+epREBMxMYwJQ0yijY9J/aOjwaJ//Lyjj6vS+wavGx9v9Xq92jXh/rUgjcWl+Wzj5w7DFY/7mMswtm6ISbTxMal/qV6+o+hV70zGPvqKC39qX9CMUCpKg33VtfVjiWer3ccX8Iax3UNMos2ICY+amBhGeohJJCa0ISaGkR5iEokJbYiJYaSHmERiQhtiYhjpISaRmNCGmBhGeohJJCa0ISaGkR5iEokJbYiJYaSHmERiQhvhFwZIS/3OrJqYAJBNTADIJiYAZBMTALKJCQDZxASAbGICQDYxASCbmACQTUwAyCYmAGQTEwCyiQkb4dSpU3NLzQMsh5iwEUIc5hliAqsVRioSbYU5xISlW2xM9ovezk6xU9PbT533KIVn3C32+qljsH7CSEWirTCHmLB0i49JbaHe75VB6RX7x857lMSEzRJGKhJthTnEhKVbakzWcuEWEzZLGKlItBXmEBOWbunvTHb3in51vL9X7I4+AqvescRr9o+OhY/G+nu78bydYnevH+dfxBzHz207f68X5mu+01rE88NkYaQi0VaYQ0xYusXHpFooS739xrHx0AwX2XhNFZ3BR2O1BXjso7JFzVE7d7C4V3NOn38035h5752aP25DQhipSLQV5hATlm5p70wGi3Ttb95jfyOPBrFpLLDTthcxx7FjD4v9XvyHAq3nr5vj3hPnr86F48JIRaKtMIeYsHRLi0m5HT7qGf3NuxmXkXkX48w5jh3rF3u79Zi0mX/asSnbE+eHycJIRaKtMIeYsHTLjMn4dnid+qho2jXN7SXMMbbAt51/2rFp25Pmh8nCSEWirTCHmLB0y41J/CJ67PuJ5kc88yzGpew5hq+HX6YP5xi8KxmcV2o1f9089y4l56/OhePCSEWirTCHmLB0i40JsGhhpCLRVphDTFi6EId5peYBlkNMAMgmJgBkExMAsokJANnEBIBsYgJANjEBIJuYAJBNTADIJiYAZBMTALKJCQDZxASAbGICQDYxASCbmACQTUwAyCYmAGQTEwCyiQkA2cQEgGxi0lEvvPAC0JD6XaEdMemo8Itz7949IBKTPGLSUWIC48Qkj5h0lJjAODHJIyYdJSYwTkzyiElHiQmME5M8YtJRYgLjxCSPmHSUmMA4MckjJh0lJjBOTPKISUeJyaKcLR7fOVWcPkgdyxXmfrw4mzzGoolJHjHpKDFZFDHZFmKSR0w6SkwWRUy2hZjkEZOOEpNFmRKTg9PFqZ2dYid6/Gz62KnTpyfMUY9JvM/px0fznTp90DifHGKSR0w6SkwWZVJMwv5aQAbxqM4bP3Zw+lQZhzYxKSPy+NnhsbMhKt61LJKY5BGTjhKTRZkQk0E8xhf7s4/HgIRjp04XB6NjE+Y4FpP6OZOu4aTEJI+YdJSYLIqYbAsxySMmHSUmizItBM2PuephOMnHXGKyTGKSR0w6SkwWZRiG6kvxsS/GBwGp9jcW/sF3HtX5c3wBLyZLIyZ5xKSjxGSNJD4SY/XEJI+YdJSYrI/wXcrO2HcoPApikkdMOkpMHqWD4vSp6uOvwLuSdSAmecSko8QExolJHjHpKDGBcWKSR0w6SkxgnJjkEZOOEhMYJyZ5xKSjxATGiUkeMekoMYFxYpJHTDoq/OIA41K/K7QjJgBkExMAsokJANnEBIBsYgJANjEBIJuYAJBNTADIJiYAZBMTALKJCQDZxASAbGICQDYxASCbmACQTUwAyCYmAGQTEwCyiQkA2cJI7Z+HmAB0nJgAkE1MAMgmJgBkExMAsokJANnEBIBsYgJANjEBIJuYAJBNTADIJiYAZBMTALKJCQDZxASAbGICQDYxASCbmACQTUwAyCYmAGQTEwCyiQkA2cQEgGxiAkA2MQEgm5gAkE1MAMgmJgBkExMAsokJANlSMen1esf2TdsvJgAdNykmzXCk9lXEBKDjpsWkikdzu0lMADouFZOgHpBpIQnEBKDjJsUkaBOSQEwAOm5aTIJZIQnEBKDjZsWkDTEB6DgxASCbmACQTUwAyLaQmAxmMQzDMDo7UnGYV9Y7k8rh4SEADan1cluJCcCSpNbLbSUmAEuSWi+3lZgALElqvdxWM2PS5n+XJfWHCNB1qfVyVdqs3Ys0NSZtHyb1hwjQdan1cpXaruGLMDEm8zxE6g8RoOtS6+WqzbOW50jGZN6bp/4QAboutV4+CvOu6SfhnQnAkqTWy1WbZy3P4TsTgCVJrZer1HYNX4SpMQnaPEzqDxGg61Lr5aq0WbsXaWZM2kj9IQJ0XWq93FZiArAkqfVyW4kJwJKk1sttJSYAS5JaL7fVQmICQJc9LP4fEQ6JtFyyBkgAAAAASUVORK5CYII=)

This cycle takes seconds per iteration since `render_form` completes in ~150ms. No build step, no waiting.

---

## 4. Testing a Running App with FlaUI

You want Claude to launch an app, interact with it, and verify behavior -- all headless.

```
> User: Launch the test app and fill in the registration form. Use "Jane Doe"
>       for the name and "jane@example.com" for email, then submit it.
```

Claude launches the app:

```
Claude calls launch_app:
  path: "C:\project\bin\Debug\net8.0-windows\MyApp.exe"

Result: { success: true, pid: 8432, processName: "MyApp" }
```

Claude waits for the main window to load:

```
Claude calls wait_for_element:
  name: "Registration"
  timeoutMs: 5000

Result: { success: true, found: true }
```

Claude finds and fills the name field:

```
Claude calls find_element:
  automationId: "txtName"

Result: { success: true, elementId: "elem_1", controlType: "Edit" }

Claude calls type_text:
  elementId: "elem_1"
  text: "Jane Doe"
  clearFirst: true

Result: { success: true }
```

Claude finds and fills the email field:

```
Claude calls find_element:
  automationId: "txtEmail"

Result: { success: true, elementId: "elem_2", controlType: "Edit" }

Claude calls type_text:
  elementId: "elem_2"
  text: "jane@example.com"
  clearFirst: true

Result: { success: true }
```

Claude clicks the submit button:

```
Claude calls find_element:
  automationId: "btnSubmit"

Result: { success: true, elementId: "elem_3", controlType: "Button" }

Claude calls click_element:
  elementId: "elem_3"

Result: { success: true }
```

Claude takes a screenshot to show you the result:

```
Claude calls take_screenshot:
  outputPath: "C:\temp\registration-result.png"

Result: { success: true }
```

```
Done. I launched the app, filled in "Jane Doe" and "jane@example.com", and
clicked Submit. Here's a screenshot of the result -- the success message
is visible at the top of the form.
```

![Registration form with data filled in](data:image/png;base64,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)

```
> User: Now close it.
```

```
Claude calls close_app:
  pid: 8432
  force: false

Result: { success: true }
```

```
App closed cleanly.
```

### Checking element properties

```
> User: Is the Submit button enabled after filling in both fields?
```

```
Claude calls get_property:
  elementId: "elem_3"
  propertyName: "isenabled"

Result: { success: true, value: "True" }
```

```
Yes, the Submit button is enabled.
```

### Keyboard shortcuts and navigation

```
> User: Test that Tab order works correctly on the form.
```

```
Claude calls click_element:
  elementId: "elem_1"    (clicks the name field to focus it)

Claude calls send_keys:
  keys: "{TAB}"          (Tab to next field)

Claude calls take_screenshot:
  outputPath: "C:\temp\tab-order-1.png"

Claude calls send_keys:
  keys: "{TAB}"          (Tab again)

Claude calls take_screenshot:
  outputPath: "C:\temp\tab-order-2.png"
```

```
Tab order is: Name field -> Email field -> Submit button. Each screenshot
shows the focus moving to the next control in sequence. The order matches
the expected flow.
```

---

## 5. Validating UI Changes

You made a code change and want to verify it looks right -- both as a static preview and at runtime.

```
> User: I just changed the main form to add a status bar. Can you verify it
>       looks right?
```

**Step 1: Static preview** -- Claude renders the designer file to check layout.

```
Claude calls render_form:
  designerFilePath: "C:\project\Forms\MainForm.Designer.cs"
  outputPath: "C:\temp\main-preview.png"
```

```
Static preview looks good. The status bar is docked to the bottom of the
form with the text "Ready" as expected. The other controls aren't overlapping
with it.
```

![Main form with status bar showing Ready](data:image/png;base64,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)

**Step 2: Runtime verification** -- Claude builds and launches to confirm it works.

```
> User: Good. Now run it and make sure the status bar updates when I click
>       the Process button.
```

Claude builds the project first, then launches:

```
Claude runs: dotnet build C:\project\MyApp.csproj

Claude calls launch_app:
  path: "C:\project\bin\Debug\net8.0-windows\MyApp.exe"

Result: { success: true, pid: 9210 }
```

Claude takes a "before" screenshot:

```
Claude calls take_screenshot:
  outputPath: "C:\temp\before-click.png"
```

Claude finds and reads the status bar text:

```
Claude calls find_element:
  automationId: "statusLabel"

Result: { success: true, elementId: "elem_1" }

Claude calls get_property:
  elementId: "elem_1"
  propertyName: "name"

Result: { value: "Ready" }
```

Claude clicks the Process button:

```
Claude calls find_element:
  automationId: "btnProcess"

Result: { success: true, elementId: "elem_2" }

Claude calls click_element:
  elementId: "elem_2"
```

Claude waits a moment, then checks the status bar again:

```
Claude calls wait_for_element:
  name: "Processing complete"
  timeoutMs: 5000

Result: { success: true, found: true }

Claude calls take_screenshot:
  outputPath: "C:\temp\after-click.png"
```

```
Verified. The status bar started at "Ready" and changed to "Processing
complete" after clicking the Process button. Here are the before and after
screenshots. Everything matches the expected behavior.

Before: status bar shows "Ready"
After: status bar shows "Processing complete"
```

| Before | After |
|--------|-------|
| ![Before - Ready](data:image/png;base64,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) | ![After - Processing complete](data:image/png;base64,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) |

Claude cleans up:

```
Claude calls close_app:
  pid: 9210
```

---

## Tips

- **Use `render_form` for fast iteration** -- at ~150ms per render, you can tweak layouts interactively without waiting for builds.
- **Always use `wait_for_element` before interacting** -- apps take time to load, and elements may not be immediately available.
- **Use `clearFirst: true` when typing into fields** -- avoids appending to existing text.
- **Take screenshots to verify state** -- Claude can see the screenshots and tell you what's on screen.
- **Close apps when done** -- call `close_app` to clean up resources and avoid orphan processes.
- **Use `render_form` for instant previews** -- it works on any framework version and doesn't require the project to build.

---

For setup instructions, see [Claude Code Setup Guide](CLAUDE_CODE_SETUP.md). For the main project documentation, see the [README](../README.md).
