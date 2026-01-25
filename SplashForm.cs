using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace AnonPDF
{
    public sealed class SplashForm : Form
    {
        private const int BorderThickness = 2;
        private const int ShadowSize = 4;
        private readonly Label licenseStatusLabel;
        private readonly Label updateStatusLabel;

        public SplashForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ControlBox = false;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            ClientSize = new Size(420, 260);
            Text = Branding.ProductName;
            DoubleBuffered = true;
            Padding = new Padding(BorderThickness, BorderThickness, BorderThickness + ShadowSize, BorderThickness + ShadowSize);

            var layout = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 9,
                Dock = DockStyle.Fill,
                Padding = new Padding(20),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 6F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 8F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 12F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var logoBox = new PictureBox
            {
                Size = new Size(64, 64),
                SizeMode = PictureBoxSizeMode.Zoom,
                Anchor = AnchorStyles.None
            };
            logoBox.Image = LoadLogoImage();

            var titleLabel = new Label
            {
                Text = Branding.ProductName,
                AutoSize = true,
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0x1F, 0x2A, 0x33),
                Anchor = AnchorStyles.None,
                TextAlign = ContentAlignment.MiddleCenter
            };

            var descriptionLabel = new Label
            {
                Text = GetDescriptionText(),
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                ForeColor = Color.FromArgb(0x55, 0x62, 0x70),
                Anchor = AnchorStyles.None,
                TextAlign = ContentAlignment.MiddleCenter
            };

            var versionLabel = new Label
            {
                Text = $"{GetVersionLabelText()}: {GetFileVersion()}",
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                ForeColor = Color.FromArgb(0x1F, 0x2A, 0x33),
                Anchor = AnchorStyles.None,
                TextAlign = ContentAlignment.MiddleCenter
            };

            licenseStatusLabel = new Label
            {
                Text = GetLicenseStatusText(),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.25F, FontStyle.Regular),
                ForeColor = Color.FromArgb(0x55, 0x62, 0x70),
                Anchor = AnchorStyles.None,
                TextAlign = ContentAlignment.MiddleCenter
            };

            updateStatusLabel = new Label
            {
                Text = GetUpdateStatusText(),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.25F, FontStyle.Regular),
                ForeColor = Color.FromArgb(0x55, 0x62, 0x70),
                Anchor = AnchorStyles.None,
                TextAlign = ContentAlignment.MiddleCenter
            };

            layout.Controls.Add(logoBox, 0, 0);
            layout.Controls.Add(titleLabel, 0, 1);
            layout.Controls.Add(new Panel { Height = 6, Dock = DockStyle.Fill }, 0, 2);
            layout.Controls.Add(descriptionLabel, 0, 3);
            layout.Controls.Add(new Panel { Height = 8, Dock = DockStyle.Fill }, 0, 4);
            layout.Controls.Add(versionLabel, 0, 5);
            layout.Controls.Add(licenseStatusLabel, 0, 7);
            layout.Controls.Add(updateStatusLabel, 0, 8);

            Controls.Add(layout);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Color.FromArgb(0xC9, 0xD6, 0xDF), BorderThickness))
            {
                int inset = BorderThickness / 2;
                var rect = new Rectangle(inset, inset, Width - BorderThickness - 1, Height - BorderThickness - 1);
                e.Graphics.DrawRectangle(pen, rect);
            }

            var shadowColor = Color.FromArgb(60, 0, 0, 0);
            using (var brush = new SolidBrush(shadowColor))
            {
                var rightRect = new Rectangle(Width - ShadowSize, BorderThickness, ShadowSize, Height - ShadowSize - BorderThickness);
                var bottomRect = new Rectangle(BorderThickness, Height - ShadowSize, Width - ShadowSize - BorderThickness, ShadowSize);
                var cornerRect = new Rectangle(Width - ShadowSize, Height - ShadowSize, ShadowSize, ShadowSize);
                e.Graphics.FillRectangle(brush, rightRect);
                e.Graphics.FillRectangle(brush, bottomRect);
                e.Graphics.FillRectangle(brush, cornerRect);
            }
        }

        public void UpdateLicenseStatus(string text)
        {
            if (licenseStatusLabel == null)
            {
                return;
            }

            licenseStatusLabel.Text = text;
        }

        public void UpdateUpdateStatus(string text)
        {
            if (updateStatusLabel == null)
            {
                return;
            }

            updateStatusLabel.Text = text;
        }

        private static Image LoadLogoImage()
        {
            try
            {
                using (var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                {
                    if (icon != null)
                    {
                        return icon.ToBitmap();
                    }
                }
            }
            catch
            {
            }

            try
            {
                string iconPath = Path.Combine(Application.StartupPath, "pdf-icon.ico");
                if (File.Exists(iconPath))
                {
                    using (var icon = new Icon(iconPath))
                    {
                        return icon.ToBitmap();
                    }
                }
            }
            catch
            {
            }

            return SystemIcons.Application.ToBitmap();
        }

        private static string GetFileVersion()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                return FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion;
            }
            catch
            {
                return Application.ProductVersion;
            }
        }

        private static string GetVersionLabelText()
        {
            return IsPolishCulture() ? "Wersja" : "Version";
        }

        private static string GetDescriptionText()
        {
            var text = Properties.Resources.ResourceManager.GetString(
                "About_Description",
                System.Globalization.CultureInfo.CurrentUICulture);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            return IsPolishCulture()
                ? "Aplikacja dedykowana do anonimizacji plikow PDF"
                : "Application dedicated to anonymizing PDF files";
        }

        private static string GetLicenseStatusText()
        {
            var info = LicenseManager.Current;
            if (info == null)
            {
                return IsPolishCulture() ? "Status licencji: brak" : "License status: missing";
            }

            if (!info.IsSignatureValid)
            {
                return IsPolishCulture() ? "Status licencji: nieprawidłowa" : "License status: invalid";
            }

            if (info.Payload == null)
            {
                return IsPolishCulture() ? "Status licencji: brak danych" : "License status: no data";
            }

            if (LicenseManager.IsRevoked)
            {
                return IsPolishCulture()
                    ? "Status licencji: DEMO (licencja cofnięta)"
                    : "License status: DEMO (license revoked)";
            }

            if (LicenseManager.IsUpdateOutOfRangeForCurrentVersion)
            {
                return IsPolishCulture()
                    ? "Status licencji: DEMO (brak licencji na nowsze wersje)"
                    : "License status: DEMO (updates not licensed)";
            }

            if (string.Equals(info.Payload.Edition, "demo", StringComparison.OrdinalIgnoreCase))
            {
                var demoUntil = ParseDate(info.Payload.DemoUntil);
                if (!demoUntil.HasValue)
                {
                    return IsPolishCulture() ? "Status licencji: DEMO" : "License status: DEMO";
                }

                var daysLeft = (int)Math.Ceiling((demoUntil.Value.Date - DateTime.UtcNow.Date).TotalDays);
                if (daysLeft >= 0)
                {
                    return IsPolishCulture()
                        ? $"Status licencji: DEMO ({daysLeft} dni)"
                        : $"License status: DEMO ({daysLeft} days)";
                }

                return IsPolishCulture()
                    ? $"Status licencji: DEMO (wygasła {demoUntil:yyyy-MM-dd})"
                    : $"License status: DEMO (expired {demoUntil:yyyy-MM-dd})";
            }

            return IsPolishCulture() ? "Status licencji: PRO" : "License status: PRO";
        }

        private static string GetUpdateStatusText()
        {
            var info = LicenseManager.Current;
            if (info == null || !info.IsSignatureValid || info.Payload == null)
            {
                return IsPolishCulture() ? "Aktualizacje: brak danych" : "Updates: no data";
            }

            var updatesUntil = LicenseManager.GetEffectiveUpdatesUntil();
            if (!updatesUntil.HasValue)
            {
                return IsPolishCulture() ? "Aktualizacje: brak" : "Updates: none";
            }

            if (updatesUntil.Value.Date >= DateTime.UtcNow.Date)
            {
                return IsPolishCulture()
                    ? $"Aktualizacje: do {updatesUntil:yyyy-MM-dd}"
                    : $"Updates: until {updatesUntil:yyyy-MM-dd}";
            }

            return IsPolishCulture()
                ? $"Aktualizacje: wygasły ({updatesUntil:yyyy-MM-dd})"
                : $"Updates: expired ({updatesUntil:yyyy-MM-dd})";
        }

        private static bool IsPolishCulture()
        {
            return System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pl";
        }

        private static DateTime? ParseDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (DateTime.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime exact))
            {
                return DateTime.SpecifyKind(exact, DateTimeKind.Utc);
            }

            if (DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime parsed))
            {
                return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }

            return null;
        }    }
}

