using System;
using System.Drawing;
using System.Windows.Forms;

namespace AnonPDF
{
    internal sealed class ThemedCheckBox : CheckBox
    {
        public Color DisabledForeColor { get; set; } = SystemColors.GrayText;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (Enabled)
            {
                return;
            }

            DrawDisabledText(e.Graphics);
        }

        private void DrawDisabledText(Graphics graphics)
        {
            if (string.IsNullOrEmpty(Text))
            {
                return;
            }

            var state = GetDisabledState();
            var glyphSize = CheckBoxRenderer.GetGlyphSize(graphics, state);
            var glyphRect = GetGlyphRectangle(ClientRectangle, glyphSize, CheckAlign);
            var textRect = GetTextRectangle(ClientRectangle, glyphRect, CheckAlign);
            var flags = GetTextFormatFlags(RightToLeft, UseMnemonic);

            TextRenderer.DrawText(graphics, Text, Font, textRect, DisabledForeColor, BackColor, flags);
        }

        private System.Windows.Forms.VisualStyles.CheckBoxState GetDisabledState()
        {
            if (CheckState == CheckState.Indeterminate)
            {
                return System.Windows.Forms.VisualStyles.CheckBoxState.MixedDisabled;
            }

            return CheckState == CheckState.Checked
                ? System.Windows.Forms.VisualStyles.CheckBoxState.CheckedDisabled
                : System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedDisabled;
        }

        private static Rectangle GetGlyphRectangle(Rectangle bounds, Size glyphSize, ContentAlignment align)
        {
            int x = bounds.Left;
            int y = bounds.Top + (bounds.Height - glyphSize.Height) / 2;

            if (align == ContentAlignment.TopLeft || align == ContentAlignment.TopCenter || align == ContentAlignment.TopRight)
            {
                y = bounds.Top;
            }
            else if (align == ContentAlignment.BottomLeft || align == ContentAlignment.BottomCenter || align == ContentAlignment.BottomRight)
            {
                y = bounds.Bottom - glyphSize.Height;
            }

            if (align == ContentAlignment.TopRight || align == ContentAlignment.MiddleRight || align == ContentAlignment.BottomRight)
            {
                x = bounds.Right - glyphSize.Width;
            }
            else if (align == ContentAlignment.TopCenter || align == ContentAlignment.MiddleCenter || align == ContentAlignment.BottomCenter)
            {
                x = bounds.Left + (bounds.Width - glyphSize.Width) / 2;
            }

            return new Rectangle(x, y, glyphSize.Width, glyphSize.Height);
        }

        private static Rectangle GetTextRectangle(Rectangle bounds, Rectangle glyphRect, ContentAlignment align)
        {
            const int padding = 4;
            if (align == ContentAlignment.TopRight || align == ContentAlignment.MiddleRight || align == ContentAlignment.BottomRight)
            {
                return new Rectangle(bounds.Left, bounds.Top, glyphRect.Left - bounds.Left - padding, bounds.Height);
            }

            return new Rectangle(glyphRect.Right + padding, bounds.Top, bounds.Width - glyphRect.Right - padding, bounds.Height);
        }

        private static TextFormatFlags GetTextFormatFlags(RightToLeft rightToLeft, bool useMnemonic)
        {
            TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
            flags |= rightToLeft == RightToLeft.Yes ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left;
            if (!useMnemonic)
            {
                flags |= TextFormatFlags.NoPrefix;
            }

            return flags;
        }
    }

    internal sealed class ThemedRadioButton : RadioButton
    {
        public Color DisabledForeColor { get; set; } = SystemColors.GrayText;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (Enabled)
            {
                return;
            }

            DrawDisabledText(e.Graphics);
        }

        private void DrawDisabledText(Graphics graphics)
        {
            if (string.IsNullOrEmpty(Text))
            {
                return;
            }

            var state = GetDisabledState();
            var glyphSize = RadioButtonRenderer.GetGlyphSize(graphics, state);
            var glyphRect = GetGlyphRectangle(ClientRectangle, glyphSize, CheckAlign);
            var textRect = GetTextRectangle(ClientRectangle, glyphRect, CheckAlign);
            var flags = GetTextFormatFlags(RightToLeft, UseMnemonic);

            TextRenderer.DrawText(graphics, Text, Font, textRect, DisabledForeColor, BackColor, flags);
        }

        private System.Windows.Forms.VisualStyles.RadioButtonState GetDisabledState()
        {
            return Checked
                ? System.Windows.Forms.VisualStyles.RadioButtonState.CheckedDisabled
                : System.Windows.Forms.VisualStyles.RadioButtonState.UncheckedDisabled;
        }

        private static Rectangle GetGlyphRectangle(Rectangle bounds, Size glyphSize, ContentAlignment align)
        {
            int x = bounds.Left;
            int y = bounds.Top + (bounds.Height - glyphSize.Height) / 2;

            if (align == ContentAlignment.TopLeft || align == ContentAlignment.TopCenter || align == ContentAlignment.TopRight)
            {
                y = bounds.Top;
            }
            else if (align == ContentAlignment.BottomLeft || align == ContentAlignment.BottomCenter || align == ContentAlignment.BottomRight)
            {
                y = bounds.Bottom - glyphSize.Height;
            }

            if (align == ContentAlignment.TopRight || align == ContentAlignment.MiddleRight || align == ContentAlignment.BottomRight)
            {
                x = bounds.Right - glyphSize.Width;
            }
            else if (align == ContentAlignment.TopCenter || align == ContentAlignment.MiddleCenter || align == ContentAlignment.BottomCenter)
            {
                x = bounds.Left + (bounds.Width - glyphSize.Width) / 2;
            }

            return new Rectangle(x, y, glyphSize.Width, glyphSize.Height);
        }

        private static Rectangle GetTextRectangle(Rectangle bounds, Rectangle glyphRect, ContentAlignment align)
        {
            const int padding = 4;
            if (align == ContentAlignment.TopRight || align == ContentAlignment.MiddleRight || align == ContentAlignment.BottomRight)
            {
                return new Rectangle(bounds.Left, bounds.Top, glyphRect.Left - bounds.Left - padding, bounds.Height);
            }

            return new Rectangle(glyphRect.Right + padding, bounds.Top, bounds.Width - glyphRect.Right - padding, bounds.Height);
        }

        private static TextFormatFlags GetTextFormatFlags(RightToLeft rightToLeft, bool useMnemonic)
        {
            TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
            flags |= rightToLeft == RightToLeft.Yes ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left;
            if (!useMnemonic)
            {
                flags |= TextFormatFlags.NoPrefix;
            }

            return flags;
        }
    }

    internal sealed class ThemedGroupBox : GroupBox
    {
        public Color DisabledForeColor { get; set; } = SystemColors.GrayText;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DrawThemedText(e.Graphics);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        private void DrawThemedText(Graphics graphics)
        {
            if (string.IsNullOrEmpty(Text))
            {
                return;
            }

            var textColor = Enabled ? ForeColor : DisabledForeColor;
            var textSize = TextRenderer.MeasureText(graphics, Text, Font, Size.Empty, TextFormatFlags.SingleLine);
            var textRect = new Rectangle(8, 0, textSize.Width + 2, textSize.Height);

            using (var backBrush = new SolidBrush(BackColor))
            {
                graphics.FillRectangle(backBrush, textRect);
            }

            TextRenderer.DrawText(graphics, Text, Font, textRect, textColor, BackColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }
}
