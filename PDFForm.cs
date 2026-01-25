using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.IO;
using System.ComponentModel;
using System.Reflection;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;
using iText.Kernel.Pdf;
using iText.PdfCleanup;
using iText.Forms.Fields;
using iText.Forms;
using iText.Signatures;
using iText.Kernel.Colors;
using iText.Kernel.Pdf.Xobject;
using iText.Kernel.Pdf.Extgstate;
using iText.Commons.Bouncycastle.Cert;
using PDFiumSharp;
using Newtonsoft.Json;
using PDFiumSharp.Enums;
using System.Threading.Tasks;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Exceptions;
using System.Text.RegularExpressions;
using System.Globalization;
using AnonPDF.Properties;
using DrawingImage = System.Drawing.Image;
using DrawingRectangle = System.Drawing.Rectangle;
using KernelGeom = iText.Kernel.Geom;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using iText.IO.Font;
using iText.Kernel.Font;
using iText.Layout;
using TesseractOCR;
using System.Data.SqlClient;


// Suppress spell-check warning for project name 'AnonPDF'
#pragma warning disable SPELL
namespace AnonPDF
{
    public partial class PDFForm : Form
    {
        private static readonly string DebugLogPath = Path.Combine(Path.GetTempPath(), "AnonPDF-debug.log");
        private static bool diagnosticModeEnabled = false;
        private static bool DebugLogEnabled => diagnosticModeEnabled;
        private readonly string fileVersion;
        private string inputPdfPath = "";
        private string inputProjectPath = "";
        private int currentPage = 1;
        private int numPages = 0;
        private const int RecentFilesLimit = 10;

        private readonly string tutorialProcessName = "AnonPDFTutorial";

        private string userPassword = "";
        private string userNewPassword = null;

        private const string SignatureModeOriginal = "original";
        private const string SignatureModeRemove = "remove";
        private const string SignatureModeReport = "report";

        private readonly float searchWidthCorrection = 1.0f;
        private float scaleFactor = 0;
        private readonly float percentScaleFactor = 0.5f;
        private float minScaleFactor = 1.2f;
        private float maxScaleFactor = 3.2f;
        private readonly int markerWidth = 7;
        private readonly int markerHeight = 7;
        private System.Drawing.Point startPoint;
        private bool isDrawing;
        private bool isMoving;
        private TextAnnotation annotationToMove = null;
        private PDFiumSharp.PdfDocument pdf;
        private string lastSavedProjectName = "";
        private System.Drawing.RectangleF currentSelection;
        private List<RedactionBlock> redactionBlocks = new List<RedactionBlock>();
        private readonly HashSet<int> reencodedPages = new HashSet<int>();
        private bool projectWasChangedAfterLastSave = false;
        private readonly bool isReencodingMode;
        private readonly int reencodingDPI = 200;
        private List<SignatureInfo> signatures = new List<SignatureInfo>();
        private List<string> signaturesToRemove = new List<string>();
        private bool hasCustomSignatureSelection = false;
        private bool suppressSignatureModeChange;
        private List<TextAnnotation> textAnnotations = new List<TextAnnotation>();
        private int oldScrollValue = 0;
        private int wheelResistanceCurentValue = 0;
        private readonly int wheelResistanceMaxValue = 5;
        private HashSet<int> pagesToRemove = new HashSet<int>();
        private Dictionary<int, int> pageRotationOffsets = new Dictionary<int, int>();

        private readonly Timer zoomTimer;
        private bool zoomPending = false;
        private readonly Timer pagingTimer;
        private readonly Timer renderTimer;
        private static System.Timers.Timer versionCheckTimer;
        private static readonly HttpClient VersionHttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        private string lastNotifiedVersion = "";
        private static bool updatesOutOfRangeNotified;

        // Target scale (result of the last mouse wheel step)
        private float pendingScaleFactor;

        // Additionally track the last mouse position and computed
        // document coordinates to restore scroll after the final render
        private float pendingDocCoordX;
        private float pendingDocCoordY;
        private Point pendingMousePosInPanel;
        private bool minScaleButton;
        private bool maxScaleButton;
        private List<TextLocation> searchLocations;
        // Index of the currently selected match
        private int currentLocationIndex = -1;

        private bool pdfCleanUpToolError;

        // Fields to detect clicks on icon buttons
        private bool isClickOnIcon = false;
        private enum IconType { None, Edit, Lock, Duplicate, Delete }
        private IconType clickedIconType = IconType.None;
        private TextAnnotation annotationForIcon = null;  // annotation where the icon was clicked

        private MergeFilesForm mergeForm;

        const int annotationsIconSize = 22;
        const int annotationsIconPadding = 4;
        private const int LeftPanelBaseWidth = 187;
        private const int LeftPanelScrollbarPadding = 6;
        private static readonly System.Drawing.Color DocumentBackColor = System.Drawing.Color.White;
        private readonly SplashForm splashForm;
        private bool splashClosed;

        private enum UiThemeKind
        {
            SoftLight,
            NordCoolLight,
            BalticBreeze,
            WarmSand,
            ForestGreen,
            GraphiteDark,
            OledDarkTeal
        }

        private sealed class UiThemePalette
        {
            public UiThemePalette(
                string name,
                System.Drawing.Color windowBackColor,
                System.Drawing.Color panelBackColor,
                System.Drawing.Color sectionBackColor,
                System.Drawing.Color borderColor,
                System.Drawing.Color textPrimaryColor,
                System.Drawing.Color textSecondaryColor,
                System.Drawing.Color accentColor,
                System.Drawing.Color accentHoverColor,
                System.Drawing.Color primaryButtonBackColor,
                System.Drawing.Color primaryButtonForeColor,
                System.Drawing.Color secondaryButtonBackColor,
                System.Drawing.Color secondaryButtonForeColor,
                System.Drawing.Color selectionBackColor,
                System.Drawing.Color selectionForeColor,
                System.Drawing.Color dangerBackColor,
                System.Drawing.Color menuStripBackColor)
            {
                Name = name;
                WindowBackColor = windowBackColor;
                PanelBackColor = panelBackColor;
                SectionBackColor = sectionBackColor;
                BorderColor = borderColor;
                TextPrimaryColor = textPrimaryColor;
                TextSecondaryColor = textSecondaryColor;
                AccentColor = accentColor;
                AccentHoverColor = accentHoverColor;
                PrimaryButtonBackColor = primaryButtonBackColor;
                PrimaryButtonForeColor = primaryButtonForeColor;
                SecondaryButtonBackColor = secondaryButtonBackColor;
                SecondaryButtonForeColor = secondaryButtonForeColor;
                SelectionBackColor = selectionBackColor;
                SelectionForeColor = selectionForeColor;
                DangerBackColor = dangerBackColor;
                MenuStripBackColor = menuStripBackColor;
            }

            public string Name { get; }
            public System.Drawing.Color WindowBackColor { get; }
            public System.Drawing.Color PanelBackColor { get; }
            public System.Drawing.Color SectionBackColor { get; }
            public System.Drawing.Color BorderColor { get; }
            public System.Drawing.Color TextPrimaryColor { get; }
            public System.Drawing.Color TextSecondaryColor { get; }
            public System.Drawing.Color AccentColor { get; }
            public System.Drawing.Color AccentHoverColor { get; }
            public System.Drawing.Color PrimaryButtonBackColor { get; }
            public System.Drawing.Color PrimaryButtonForeColor { get; }
            public System.Drawing.Color SecondaryButtonBackColor { get; }
            public System.Drawing.Color SecondaryButtonForeColor { get; }
            public System.Drawing.Color SelectionBackColor { get; }
            public System.Drawing.Color SelectionForeColor { get; }
            public System.Drawing.Color DangerBackColor { get; }
            public System.Drawing.Color MenuStripBackColor { get; }
        }

        private static readonly Dictionary<UiThemeKind, UiThemePalette> UiThemes =
            new Dictionary<UiThemeKind, UiThemePalette>
            {
                {
                    UiThemeKind.SoftLight,
                    new UiThemePalette(
                        "Soft Light",
                        System.Drawing.Color.FromArgb(0xF5, 0xF6, 0xF8),
                        System.Drawing.Color.FromArgb(0xFF, 0xFF, 0xFF),
                        System.Drawing.Color.FromArgb(0xF1, 0xF3, 0xF6),
                        System.Drawing.Color.FromArgb(0xD7, 0xDC, 0xE3),
                        System.Drawing.Color.FromArgb(0x1F, 0x29, 0x37),
                        System.Drawing.Color.FromArgb(0x6B, 0x72, 0x80),
                        System.Drawing.Color.FromArgb(0x3B, 0x82, 0xF6),
                        System.Drawing.Color.FromArgb(0x25, 0x63, 0xEB),
                        System.Drawing.Color.FromArgb(0x3B, 0x82, 0xF6),
                        System.Drawing.Color.White,
                        System.Drawing.Color.FromArgb(0xE9, 0xEE, 0xF7),
                        System.Drawing.Color.FromArgb(0x1F, 0x29, 0x37),
                        System.Drawing.Color.FromArgb(0xDC, 0xEB, 0xFF),
                        System.Drawing.Color.FromArgb(0x0B, 0x3A, 0x87),
                        System.Drawing.Color.FromArgb(0xEF, 0x44, 0x44),
                        System.Drawing.Color.FromArgb(0xF1, 0xF5, 0xF9))
                },
                {
                    UiThemeKind.NordCoolLight,
                    new UiThemePalette(
                        "Nord Cool Light",
                        System.Drawing.Color.FromArgb(0xEC, 0xEF, 0xF4),
                        System.Drawing.Color.FromArgb(0xFF, 0xFF, 0xFF),
                        System.Drawing.Color.FromArgb(0xE5, 0xE9, 0xF0),
                        System.Drawing.Color.FromArgb(0xD8, 0xDE, 0xE9),
                        System.Drawing.Color.FromArgb(0x2E, 0x34, 0x40),
                        System.Drawing.Color.FromArgb(0x5E, 0x67, 0x77),
                        System.Drawing.Color.FromArgb(0x5E, 0x81, 0xAC),
                        System.Drawing.Color.FromArgb(0x4C, 0x6A, 0x91),
                        System.Drawing.Color.FromArgb(0x5E, 0x81, 0xAC),
                        System.Drawing.Color.White,
                        System.Drawing.Color.FromArgb(0xE5, 0xE9, 0xF0),
                        System.Drawing.Color.FromArgb(0x2E, 0x34, 0x40),
                        System.Drawing.Color.FromArgb(0xD8, 0xE7, 0xFF),
                        System.Drawing.Color.FromArgb(0x2E, 0x34, 0x40),
                        System.Drawing.Color.FromArgb(0xBF, 0x61, 0x6A),
                        System.Drawing.Color.FromArgb(0xE5, 0xE9, 0xF0))
                },
                {
                    UiThemeKind.BalticBreeze,
                    new UiThemePalette(
                        "Baltic Breeze",
                        System.Drawing.Color.FromArgb(0xE6, 0xEF, 0xF3),
                        System.Drawing.Color.FromArgb(0x65, 0xA0, 0xB8),
                        System.Drawing.Color.FromArgb(0xE0, 0xE3, 0xE4),
                        System.Drawing.Color.FromArgb(0xC9, 0xD6, 0xDF),
                        System.Drawing.Color.FromArgb(0x1F, 0x2A, 0x33),
                        System.Drawing.Color.FromArgb(0x55, 0x62, 0x70),
                        System.Drawing.Color.FromArgb(0x2F, 0x6F, 0x88),
                        System.Drawing.Color.FromArgb(0x25, 0x5A, 0x6E),
                        System.Drawing.Color.FromArgb(0xF9, 0xFB, 0xFB),
                        System.Drawing.Color.FromArgb(0x23, 0x40, 0x4A),
                        System.Drawing.Color.FromArgb(0xF9, 0xFB, 0xFB),
                        System.Drawing.Color.FromArgb(0x1F, 0x2A, 0x33),
                        System.Drawing.Color.FromArgb(0xC7, 0xE1, 0xEC),
                        System.Drawing.Color.FromArgb(0x12, 0x40, 0x52),
                        System.Drawing.Color.FromArgb(0xD6, 0x45, 0x45),
                        System.Drawing.Color.FromArgb(0xF9, 0xFB, 0xFB))
                },
                {
                    UiThemeKind.WarmSand,
                    new UiThemePalette(
                        "Warm Sand",
                        System.Drawing.Color.FromArgb(0xF6, 0xF1, 0xEA),
                        System.Drawing.Color.FromArgb(0xFF, 0xFC, 0xF7),
                        System.Drawing.Color.FromArgb(0xF1, 0xE8, 0xDD),
                        System.Drawing.Color.FromArgb(0xE2, 0xD6, 0xC7),
                        System.Drawing.Color.FromArgb(0x2B, 0x2A, 0x28),
                        System.Drawing.Color.FromArgb(0x6A, 0x62, 0x5A),
                        System.Drawing.Color.FromArgb(0xC0, 0x84, 0x57),
                        System.Drawing.Color.FromArgb(0xA7, 0x6B, 0x41),
                        System.Drawing.Color.FromArgb(0xC0, 0x84, 0x57),
                        System.Drawing.Color.White,
                        System.Drawing.Color.FromArgb(0xEF, 0xE4, 0xD8),
                        System.Drawing.Color.FromArgb(0x2B, 0x2A, 0x28),
                        System.Drawing.Color.FromArgb(0xF2, 0xD9, 0xC6),
                        System.Drawing.Color.FromArgb(0x5B, 0x34, 0x1C),
                        System.Drawing.Color.FromArgb(0xD6, 0x45, 0x45),
                        System.Drawing.Color.FromArgb(0xF1, 0xE8, 0xDD))
                },
                {
                    UiThemeKind.ForestGreen,
                    new UiThemePalette(
                        "Forest Green",
                        System.Drawing.Color.FromArgb(0xF3, 0xF6, 0xF4),
                        System.Drawing.Color.FromArgb(0xFF, 0xFF, 0xFF),
                        System.Drawing.Color.FromArgb(0xE7, 0xEF, 0xEA),
                        System.Drawing.Color.FromArgb(0xD3, 0xDE, 0xD7),
                        System.Drawing.Color.FromArgb(0x1F, 0x2A, 0x24),
                        System.Drawing.Color.FromArgb(0x5C, 0x6B, 0x63),
                        System.Drawing.Color.FromArgb(0x2F, 0x85, 0x5A),
                        System.Drawing.Color.FromArgb(0x27, 0x67, 0x49),
                        System.Drawing.Color.FromArgb(0x2F, 0x85, 0x5A),
                        System.Drawing.Color.White,
                        System.Drawing.Color.FromArgb(0xE1, 0xEE, 0xE7),
                        System.Drawing.Color.FromArgb(0x1F, 0x2A, 0x24),
                        System.Drawing.Color.FromArgb(0xCF, 0xEB, 0xDD),
                        System.Drawing.Color.FromArgb(0x14, 0x5A, 0x32),
                        System.Drawing.Color.FromArgb(0xE5, 0x3E, 0x3E),
                        System.Drawing.Color.FromArgb(0xE7, 0xEF, 0xEA))
                },
                {
                    UiThemeKind.GraphiteDark,
                    new UiThemePalette(
                        "Graphite Dark",
                        System.Drawing.Color.FromArgb(0x0F, 0x17, 0x2A),
                        System.Drawing.Color.FromArgb(0x11, 0x18, 0x27),
                        System.Drawing.Color.FromArgb(0x17, 0x20, 0x33),
                        System.Drawing.Color.FromArgb(0x2B, 0x36, 0x48),
                        System.Drawing.Color.FromArgb(0xE5, 0xE7, 0xEB),
                        System.Drawing.Color.FromArgb(0x9C, 0xA3, 0xAF),
                        System.Drawing.Color.FromArgb(0x60, 0xA5, 0xFA),
                        System.Drawing.Color.FromArgb(0x3B, 0x82, 0xF6),
                        System.Drawing.Color.FromArgb(0x25, 0x63, 0xEB),
                        System.Drawing.Color.White,
                        System.Drawing.Color.FromArgb(0x32, 0x4A, 0x6B),
                        System.Drawing.Color.FromArgb(0xE5, 0xE7, 0xEB),
                        System.Drawing.Color.FromArgb(0x1E, 0x3A, 0x8A),
                        System.Drawing.Color.FromArgb(0xE5, 0xE7, 0xEB),
                        System.Drawing.Color.FromArgb(0xF8, 0x71, 0x71),
                        System.Drawing.Color.FromArgb(0x17, 0x20, 0x33))
                },
                {
                    UiThemeKind.OledDarkTeal,
                    new UiThemePalette(
                        "Dark Teal",
                        System.Drawing.Color.FromArgb(0x0B, 0x0F, 0x14),
                        System.Drawing.Color.FromArgb(0x0F, 0x17, 0x20),
                        System.Drawing.Color.FromArgb(0x12, 0x1E, 0x2A),
                        System.Drawing.Color.FromArgb(0x22, 0x32, 0x44),
                        System.Drawing.Color.FromArgb(0xE6, 0xED, 0xF3),
                        System.Drawing.Color.FromArgb(0x9A, 0xA7, 0xB2),
                        System.Drawing.Color.FromArgb(0x22, 0xC1, 0xC3),
                        System.Drawing.Color.FromArgb(0x14, 0xA6, 0xA8),
                        System.Drawing.Color.FromArgb(0x22, 0xC1, 0xC3),
                        System.Drawing.Color.FromArgb(0x06, 0x20, 0x24),
                        System.Drawing.Color.FromArgb(0x2D, 0x43, 0x58),
                        System.Drawing.Color.FromArgb(0xE6, 0xED, 0xF3),
                        System.Drawing.Color.FromArgb(0x0E, 0x5E, 0x63),
                        System.Drawing.Color.FromArgb(0xE6, 0xED, 0xF3),
                        System.Drawing.Color.FromArgb(0xFF, 0x6B, 0x6B),
                        System.Drawing.Color.FromArgb(0x12, 0x1E, 0x2A))
                }
            };

        private UiThemeKind currentThemeKind = UiThemeKind.BalticBreeze;
        private UiThemePalette CurrentTheme => UiThemes[currentThemeKind];

        private sealed class MenuColorTable : ProfessionalColorTable
        {
            private readonly UiThemePalette theme;

            public MenuColorTable(UiThemePalette theme)
            {
                this.theme = theme;
                UseSystemColors = false;
            }

            public override System.Drawing.Color MenuStripGradientBegin => theme.MenuStripBackColor;
            public override System.Drawing.Color MenuStripGradientEnd => theme.MenuStripBackColor;
            public override System.Drawing.Color ToolStripDropDownBackground => theme.SectionBackColor;
            public override System.Drawing.Color ImageMarginGradientBegin => theme.SectionBackColor;
            public override System.Drawing.Color ImageMarginGradientMiddle => theme.SectionBackColor;
            public override System.Drawing.Color ImageMarginGradientEnd => theme.SectionBackColor;
            public override System.Drawing.Color MenuItemSelected => theme.SelectionBackColor;
            public override System.Drawing.Color MenuItemSelectedGradientBegin => theme.SelectionBackColor;
            public override System.Drawing.Color MenuItemSelectedGradientEnd => theme.SelectionBackColor;
            public override System.Drawing.Color MenuItemPressedGradientBegin => theme.SectionBackColor;
            public override System.Drawing.Color MenuItemPressedGradientEnd => theme.SectionBackColor;
            public override System.Drawing.Color MenuItemBorder => theme.BorderColor;
            public override System.Drawing.Color SeparatorDark => theme.BorderColor;
            public override System.Drawing.Color SeparatorLight => theme.BorderColor;
            public override System.Drawing.Color ToolStripBorder => theme.BorderColor;
            public override System.Drawing.Color MenuBorder => theme.BorderColor;
        }
        private bool isFullScreen = false;
        private FormBorderStyle previousFormBorderStyle;
        private FormWindowState previousWindowState;
        private System.Drawing.Rectangle previousBounds;
        private bool previousTopMost;

        private List<PageItemStatus> allPageStatuses = new List<PageItemStatus>();
        private string allComboItem = Resources.UI_Filter_AllPages;
        private string allCategories = Resources.UI_Filter_AllCategories;

        // Mapping: list item text → color
        private readonly Dictionary<string, System.Drawing.Color> _comboItemColors = new Dictionary<string, System.Drawing.Color>();
        // Required WinAPI functions
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int SW_RESTORE = 9;
        private const int DWMWA_CAPTION_COLOR = 35;

        public PDFForm(SplashForm splash = null)
        {
            splashForm = splash;
            // Read version info
            var assembly = Assembly.GetExecutingAssembly();
            var fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
            fileVersion = fileVersionInfo.FileVersion;

            InitializeComponent();
            LoadPreferredTheme();
            ApplyTheme();
            this.HandleCreated += (_, __) => ApplyTitleBarColor();

            // Apply preferred UI culture if set; otherwise use environment (OS) culture
            var prefCulture = Properties.Settings.Default.PreferredUICulture;
            if (!string.IsNullOrWhiteSpace(prefCulture))
            {
                SetLanguage(prefCulture);
            }
            else
            {
                // Use current OS UI culture for "System" default (user's display language)
                var sys = CultureInfo.CurrentUICulture;
                System.Threading.Thread.CurrentThread.CurrentUICulture = sys;
                System.Threading.Thread.CurrentThread.CurrentCulture = sys;
                AnonPDF.Properties.Resources.Culture = sys;
                ApplyLocalization();
                UpdateHelpMenuAvailability();
            }

            UpdateWindowTitle();

            this.KeyPreview = true;

            searchLocations = new List<TextLocation>();

            isReencodingMode = true;

            // Initialize zoom timer: render at most every 150ms while zooming
            zoomTimer = new Timer
            {
                Interval = 150
            };
            zoomTimer.Tick += ZoomTimer_Tick;

            pagingTimer = new Timer
            {
                Interval = 300
            };
            pagingTimer.Tick += PagingTimer_Tick;

            renderTimer = new Timer
            {
                Interval = 2000
            };
            renderTimer.Tick += RenderTimer_Tick;

            // Version check timer – check every 10 minutes
            versionCheckTimer = new System.Timers.Timer(10 * 60 * 1000);
            versionCheckTimer.Elapsed += OnVersionCheck;
            versionCheckTimer.AutoReset = true;
            versionCheckTimer.Start();

            // Enable drag-and-drop of files on the entire form
            this.AllowDrop = true;

            // Wire up DragEnter and DragDrop events
            this.DragEnter += PDFForm_DragEnter;
            this.DragDrop += PDFForm_DragDrop;

            // Handle KeyDown for global shortcuts
            this.KeyDown += PDFForm_KeyDown;

            // Mouse events for selection and annotation operations
            pdfViewer.MouseDown += OnMouseDown;
            pdfViewer.MouseMove += OnMouseMove;
            pdfViewer.MouseUp += OnMouseUp;
            pdfViewer.Paint += OnPaint;
            this.Shown += PDFForm_Shown;
            this.Closing += new CancelEventHandler(MainWindow_Closing);

            pagesListView.View = View.List;
            pagesListView.MultiSelect = false;

            filterComboBox.SelectedIndex = 0;            
            filterComboBox.DrawMode = DrawMode.OwnerDrawFixed;
            filterComboBox.SelectedIndexChanged += FilterComboBox_SelectedIndexChanged;
            filterComboBox.DrawItem += FilterComboBox_DrawItem;

            zoomMinButton.Text = "\uE7C3";
            zoomMaxButton.Text = "\uE7C3";
            zoomOutButton.Text = "\uE71F";
            zoomInButton.Text = "\uE8A3";
            removePageButton.Text = "\uE74D";
            removePageRangeButton.Text = " \uE74D\n1...n";

            searchToSelectionButton.Text = "\uE7B5";
            searchButton.Text = "\uE721";

            openSavedPDFCheckBox.CheckedChanged += OpenSavedPdfCheckBox_CheckedChanged;
            safeModeCheckBox.CheckedChanged += SafeModeCheckBox_CheckedChanged;
            colorCheckBox.CheckedChanged += ColorCheckBox_CheckedChanged;
            signaturesRemoveRadioButton.CheckedChanged += SignaturesRemoveRadioButton_CheckedChanged;
            signaturesOriginalRadioButton.CheckedChanged += SignaturesOriginalRadioButton_CheckedChanged;
            signaturesReportRadioButton.CheckedChanged += SignaturesReportRadioButton_CheckedChanged;

            removePageButton.Click += RemovePageButton_Click;
            removePageRangeButton.Click += RemovePageRangeButton_Click;
            saveProjectButton.EnabledChanged += (_, __) =>
            {
                UpdateSaveGroupState();
                ApplySecondaryButtonTheme(saveProjectButton, CurrentTheme);
            };
            saveProjectAsButton.EnabledChanged += (_, __) =>
            {
                UpdateSaveGroupState();
                ApplySecondaryButtonTheme(saveProjectAsButton, CurrentTheme);
            };
            buttonRedactText.EnabledChanged += (_, __) =>
            {
                UpdateSaveGroupState();
                ApplySecondaryButtonTheme(buttonRedactText, CurrentTheme);
            };
            savePdfMenuItem.EnabledChanged += (_, __) => UpdateSaveGroupState();

            buttonFirst.EnabledChanged += (_, __) => ApplySecondaryButtonTheme(buttonFirst, CurrentTheme);
            buttonPrevious.EnabledChanged += (_, __) => ApplySecondaryButtonTheme(buttonPrevious, CurrentTheme);
            buttonNextPage.EnabledChanged += (_, __) => ApplySecondaryButtonTheme(buttonNextPage, CurrentTheme);
            buttonLast.EnabledChanged += (_, __) => ApplySecondaryButtonTheme(buttonLast, CurrentTheme);
            zoomMinButton.EnabledChanged += (_, __) => ApplySecondaryButtonTheme(zoomMinButton, CurrentTheme);
            zoomMaxButton.EnabledChanged += (_, __) => ApplySecondaryButtonTheme(zoomMaxButton, CurrentTheme);
            zoomOutButton.EnabledChanged += (_, __) => ApplySecondaryButtonTheme(zoomOutButton, CurrentTheme);
            zoomInButton.EnabledChanged += (_, __) => ApplySecondaryButtonTheme(zoomInButton, CurrentTheme);

            searchButton.EnabledChanged += (_, __) => ApplySecondaryButtonTheme(searchButton, CurrentTheme);
            searchToSelectionButton.EnabledChanged += (_, __) => ApplySecondaryButtonTheme(searchToSelectionButton, CurrentTheme);
            SearchClearButton.EnabledChanged += (_, __) => ApplySecondaryButtonTheme(SearchClearButton, CurrentTheme);
            personalDataButton.EnabledChanged += (_, __) => ApplySecondaryButtonTheme(personalDataButton, CurrentTheme);
            searchFirstButton.EnabledChanged += (_, __) => ApplySecondaryButtonTheme(searchFirstButton, CurrentTheme);
            searchPrevButton.EnabledChanged += (_, __) => ApplySecondaryButtonTheme(searchPrevButton, CurrentTheme);
            searchNextButton.EnabledChanged += (_, __) => ApplySecondaryButtonTheme(searchNextButton, CurrentTheme);
            searchLastButton.EnabledChanged += (_, __) => ApplySecondaryButtonTheme(searchLastButton, CurrentTheme);

            selectionFirstButton.EnabledChanged += (_, __) => ApplySecondaryButtonTheme(selectionFirstButton, CurrentTheme);
            selectionPrevButton.EnabledChanged += (_, __) => ApplySecondaryButtonTheme(selectionPrevButton, CurrentTheme);
            selectionNextButton.EnabledChanged += (_, __) => ApplySecondaryButtonTheme(selectionNextButton, CurrentTheme);
            selectionLastButton.EnabledChanged += (_, __) => ApplySecondaryButtonTheme(selectionLastButton, CurrentTheme);
            clearPageButton.EnabledChanged += (_, __) => ApplySecondaryButtonTheme(clearPageButton, CurrentTheme);
            colorCheckBox.EnabledChanged += (_, __) => UpdateOptionsGroupState();
            openSavedPDFCheckBox.EnabledChanged += (_, __) => UpdateOptionsGroupState();
            safeModeCheckBox.EnabledChanged += (_, __) => UpdateOptionsGroupState();
            setSavePassword.EnabledChanged += (_, __) => UpdateOptionsGroupState();

            // Load last session state
            colorCheckBox.Checked = Properties.Settings.Default.LastColorCheckBoxState;
            openSavedPDFCheckBox.Checked = Properties.Settings.Default.LastOpenSavedPDFCheckBoxState;
            signaturesRemoveRadioButton.Checked = Properties.Settings.Default.LastSignaturesRemoveRadioButton;
            signaturesOriginalRadioButton.Checked = Properties.Settings.Default.LastSignaturesOriginalRadioButton;
            signaturesReportRadioButton.Checked = Properties.Settings.Default.LastSignaturesRaportRadioButton;

            mainAppSplitContainer.Panel1.AutoScroll = true;
            mainAppSplitContainer.Panel2.AutoScroll = false;
            mainAppSplitContainer.Panel1.SizeChanged += (_, __) => UpdateLeftPanelWidth();
            UpdateSaveGroupState();
            UpdateOptionsGroupState();

            PdfTextSearcher.OnCacheStatusChanged += status => {

                searchResultLabel.Invoke((MethodInvoker)(() =>
                    searchResultLabel.Text = status
                ));
                searchResultLabel.Invoke((MethodInvoker)(() =>
                    searchResultLabel.Refresh()
                ));
            };

        }

        private static bool IsEnglish()
        {
            try { return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en"; }
            catch { return false; }
        }

        private static string L(string pl, string en)
        {
            return IsEnglish() ? en : pl;
        }

        private void ApplyLocalization()
        {
            // Use already-set Resources.Culture (SetLanguage/startup decide it)
            var currentCulture = AnonPDF.Properties.Resources.Culture ?? CultureInfo.CurrentUICulture;

            // Top-level menus (from resources)
            menuFileItem.Text = Resources.Menu_File;
            menuOptionsItem.Text = Resources.Menu_Options;
            menuHelpItem.Text = Resources.Menu_Help;

            // Language submenu
            languageToolStripMenuItem.Text = Resources.Menu_Language;
            languageSystemToolStripMenuItem.Text = Resources.Menu_Language_System;
            languageEnglishToolStripMenuItem.Text = Resources.Menu_Language_English;
            languagePolishToolStripMenuItem.Text = Resources.Menu_Language_Polish;
            bool isSystem = string.IsNullOrWhiteSpace(Properties.Settings.Default.PreferredUICulture);
            languageSystemToolStripMenuItem.Checked = isSystem;
            languageEnglishToolStripMenuItem.Checked = !isSystem && currentCulture.TwoLetterISOLanguageName == "en";
            languagePolishToolStripMenuItem.Checked = !isSystem && currentCulture.TwoLetterISOLanguageName == "pl";

            // File menu
            openPdfToolStripMenuItem.Text = Resources.Menu_OpenPdf;
            openProjectToolStripMenuItem.Text = Resources.Menu_OpenProject;
            openLastPdfProjectToolStripMenuItem.Text = Resources.Menu_OpenLast;
            saveProjectAsMenuItem.Text = Resources.Menu_SaveProjectAs;
            saveProjectMenuItem.Text = Resources.Menu_SaveProject;
            savePdfMenuItem.Text = Resources.Menu_SavePdf;
            recentFilesMenuItem.Text = Resources.Menu_RecentFiles;
            exitMenuItem.Text = Resources.Menu_Exit;

            // Options menu
              splitPdfToolStripMenuItem.Text = Resources.Menu_SplitPdf;
              mergePdfToolStripMenuItem.Text = Resources.Menu_MergePdf;
              deletePageMenuItem.Text = Resources.Menu_DeletePage;
              rotatePageMenuItem.Text = Resources.Menu_RotatePage;
              addTextMenuItem.Text = Resources.Menu_AddText;
              copyToClipboardMenuItem.Text = Resources.Menu_CopyToClipboard;
              exportGraphicsMenuItem.Text = Resources.Menu_ExportGraphics;
              selectSignaturesToRemoveMenuItem.Text = Resources.Menu_SelectSignaturesToRemove;
              ignorePdfRestrictionsToolStripMenuItem.Text = Resources.Menu_IgnorePdfRestrictions;
              var themeText = Resources.ResourceManager.GetString("Menu_Options_Theme", currentCulture);
              if (!string.IsNullOrWhiteSpace(themeText))
              {
                  themeToolStripMenuItem.Text = themeText;
              }
              string GetThemeMenuText(string key, string fallback)
              {
                  var text = Resources.ResourceManager.GetString(key, currentCulture);
                  return string.IsNullOrWhiteSpace(text) ? fallback : text;
              }

              themeSoftLightMenuItem.Text = GetThemeMenuText("Theme_SoftLight", UiThemes[UiThemeKind.SoftLight].Name);
              themeNordCoolLightMenuItem.Text = GetThemeMenuText("Theme_NordCoolLight", UiThemes[UiThemeKind.NordCoolLight].Name);
              themeBalticBreezeMenuItem.Text = GetThemeMenuText("Theme_BalticBreeze", UiThemes[UiThemeKind.BalticBreeze].Name);
              themeWarmSandMenuItem.Text = GetThemeMenuText("Theme_WarmSand", UiThemes[UiThemeKind.WarmSand].Name);
              themeForestGreenMenuItem.Text = GetThemeMenuText("Theme_ForestGreen", UiThemes[UiThemeKind.ForestGreen].Name);
              themeGraphiteDarkMenuItem.Text = GetThemeMenuText("Theme_GraphiteDark", UiThemes[UiThemeKind.GraphiteDark].Name);
              themeOledDarkTealMenuItem.Text = GetThemeMenuText("Theme_OledDarkTeal", UiThemes[UiThemeKind.OledDarkTeal].Name);
              var fullScreenText = Resources.ResourceManager.GetString("Menu_Options_FullScreen", currentCulture);
              if (!string.IsNullOrWhiteSpace(fullScreenText))
              {
                  fullScreenToolStripMenuItem.Text = fullScreenText;
              }

            // Help menu
            helpMenuItem.Text = Resources.Menu_Help_Help;
            tutorialMenuItem.Text = Resources.Menu_Help_Tutorial;
            diagnosticModeMenuItem.Text = Resources.Menu_Help_DiagnosticMode;
            aboutMenuItem.Text = Resources.Menu_Help_About;
            showLicenseToolStripMenuItem.Text = Resources.Menu_Help_ShowLicense;
            thirdPartyNoticesToolStripMenuItem.Text = Resources.Menu_Help_ThirdParty;

            // Common buttons (partial coverage)
            try { buttonRedactText.Text = Resources.UI_Button_SavePdf; } catch { }
            try { clearPageButton.Text = Resources.UI_ClearPage; } catch { }
            try { clearSelectionButton.Text = Resources.UI_ClearAll; } catch { }
            try { loadPdfButton.Text = Resources.UI_Button_OpenPdf; } catch { }
            try { openProjectButton.Text = Resources.UI_Button_OpenProject; } catch { }
            try { saveProjectButton.Text = Resources.UI_Button_SaveProject; } catch { }
            try { saveProjectAsButton.Text = Resources.UI_Button_SaveProjectAs; } catch { }
            try { personalDataButton.Text = Resources.UI_Button_PersonalData; } catch { }

            // Checkboxes and radios
            try { colorCheckBox.Text = Resources.UI_Check_HighlightColor; } catch { }
            try { openSavedPDFCheckBox.Text = Resources.UI_Check_PreviewAfterSave; } catch { }
            try { safeModeCheckBox.Text = Resources.UI_Check_SafeMode; } catch { }
            try { setSavePassword.Text = Resources.UI_Check_SetPassword; } catch { }
            try { signaturesRemoveRadioButton.Text = Resources.UI_Radio_Signatures_Remove; } catch { }
            try { signaturesOriginalRadioButton.Text = Resources.UI_Radio_Signatures_Original; } catch { }
            try { signaturesReportRadioButton.Text = Resources.UI_Radio_Signatures_Report; } catch { }

            // Group boxes
            var openGroupText = Resources.ResourceManager.GetString("UI_Group_Open", currentCulture);
            if (!string.IsNullOrWhiteSpace(openGroupText))
            {
                groupBoxOpen.Text = openGroupText;
            }
            var saveGroupText = Resources.ResourceManager.GetString("UI_Group_Save", currentCulture);
            if (!string.IsNullOrWhiteSpace(saveGroupText))
            {
                groupBoxSave.Text = saveGroupText;
            }
            var optionsGroupText = Resources.ResourceManager.GetString("UI_Group_Options", currentCulture);
            if (!string.IsNullOrWhiteSpace(optionsGroupText))
            {
                groupBoxOptions.Text = optionsGroupText;
            }
            try { groupBoxSelections.Text = Resources.UI_Group_Selections; } catch { }
            try { groupBoxSignatures.Text = Resources.UI_Group_Signatures; } catch { }
            try { groupBoxSearch.Text = Resources.UI_Group_Search; } catch { }
            try { groupBoxPages.Text = Resources.UI_Group_Pages; } catch { }
            try { groupBoxPagesToRemove.Text = Resources.UI_Group_PagesToRemove; } catch { }
            try { groupBoxFilter.Text = Resources.UI_Group_Filter; } catch { }

            UpdateWindowTitle();

            // Tooltips
            try { toolTip1.SetToolTip(loadPdfButton, Resources.Tooltip_LoadPdf); } catch { }
            try { toolTip1.SetToolTip(setSavePassword, Resources.Tooltip_SetPassword); } catch { }
            try { toolTip1.SetToolTip(safeModeCheckBox, Resources.Tooltip_SafeMode); } catch { }
            try { toolTip1.SetToolTip(removePageButton, Resources.Tooltip_RemovePage); } catch { }
            try { toolTip1.SetToolTip(removePageRangeButton, Resources.Tooltip_RemovePageRange); } catch { }
            try { toolTip1.SetToolTip(signaturesReportRadioButton, Resources.Tooltip_Signatures_Report); } catch { }
            try { toolTip1.SetToolTip(signaturesOriginalRadioButton, Resources.Tooltip_Signatures_Original); } catch { }
            try { toolTip1.SetToolTip(signaturesRemoveRadioButton, Resources.Tooltip_Signatures_Remove); } catch { }
            try { toolTip1.SetToolTip(pageNumberTextBox, Resources.Tooltip_PageNumber); } catch { }
            try { toolTip1.SetToolTip(buttonFirst, Resources.Tooltip_FirstPage); } catch { }
            try { toolTip1.SetToolTip(buttonNextPage, Resources.Tooltip_NextPage); } catch { }
            try { toolTip1.SetToolTip(buttonPrevious, Resources.Tooltip_PrevPage); } catch { }
            try { toolTip1.SetToolTip(buttonLast, Resources.Tooltip_LastPage); } catch { }
            try { toolTip1.SetToolTip(zoomMinButton, Resources.Tooltip_ZoomMin); } catch { }
            try { toolTip1.SetToolTip(zoomMaxButton, Resources.Tooltip_ZoomMax); } catch { }
            try { toolTip1.SetToolTip(zoomOutButton, Resources.Tooltip_ZoomOut); } catch { }
            try { toolTip1.SetToolTip(zoomInButton, Resources.Tooltip_ZoomIn); } catch { }
            try { toolTip1.SetToolTip(searchButton, Resources.Tooltip_Search); } catch { }
            try { toolTip1.SetToolTip(searchToSelectionButton, Resources.Tooltip_SearchToSelection); } catch { }
            try { toolTip1.SetToolTip(SearchClearButton, Resources.Tooltip_SearchClear); } catch { }
            try { toolTip1.SetToolTip(personalDataButton, Resources.Tooltip_PersonalData); } catch { }
            try { toolTip1.SetToolTip(openSavedPDFCheckBox, Resources.Tooltip_PreviewAfterSave); } catch { }
            try { toolTip1.SetToolTip(saveProjectButton, Resources.Tooltip_SaveProject); } catch { }
            try { toolTip1.SetToolTip(saveProjectAsButton, Resources.Tooltip_SaveProjectAs); } catch { }
            try { toolTip1.SetToolTip(openProjectButton, Resources.Tooltip_OpenProject); } catch { }
            try { toolTip1.SetToolTip(colorCheckBox, Resources.Tooltip_HighlightColor); } catch { }
            try { toolTip1.SetToolTip(selectionFirstButton, Resources.Tooltip_SelectionFirst); } catch { }
            try { toolTip1.SetToolTip(selectionNextButton, Resources.Tooltip_SelectionNext); } catch { }
            try { toolTip1.SetToolTip(selectionPrevButton, Resources.Tooltip_SelectionPrev); } catch { }
            try { toolTip1.SetToolTip(selectionLastButton, Resources.Tooltip_SelectionLast); } catch { }
            try { toolTip1.SetToolTip(markerRadioButton, Resources.Tooltip_Marker); } catch { }
            try { toolTip1.SetToolTip(boxRadioButton, Resources.Tooltip_Box); } catch { }
            try { toolTip1.SetToolTip(clearPageButton, Resources.Tooltip_ClearPage); } catch { }
            try { toolTip1.SetToolTip(clearSelectionButton, Resources.Tooltip_ClearAll); } catch { }
            try { toolTip1.SetToolTip(buttonRedactText, Resources.Tooltip_SavePdf); } catch { }
            try { toolTip1.SetToolTip(pagesListView, Resources.Tooltip_PagesList); } catch { }

            // Filter combo: localized items and colors
            allComboItem = Resources.UI_Filter_AllPages;
            allCategories = Resources.UI_Filter_AllCategories;
            var filterSelections = Resources.UI_Filter_Selections;
            var filterAnnotations = Resources.UI_Filter_Annotations;
            var filterSearches = Resources.UI_Filter_Searches;
            var filterDeletions = Resources.UI_Filter_Deletions;
            var filterRotations = Resources.UI_Filter_Rotations;

            int selIndex = filterComboBox.SelectedIndex;
            filterComboBox.Items.Clear();
            filterComboBox.Items.AddRange(new object[]
            {
                allComboItem,
                allCategories,
                filterSelections,
                filterSearches,
                filterDeletions,
                filterRotations,
                filterAnnotations
            });
            filterComboBox.SelectedIndex = selIndex >= 0 && selIndex < filterComboBox.Items.Count ? selIndex : 0;

            _comboItemColors.Clear();
            _comboItemColors[allComboItem] = System.Drawing.Color.Black;
            _comboItemColors[allCategories] = System.Drawing.Color.Black;
            _comboItemColors[filterSelections] = System.Drawing.Color.Red;
            _comboItemColors[filterAnnotations] = System.Drawing.Color.Green;
            _comboItemColors[filterSearches] = System.Drawing.Color.FromArgb(255, 255, 215, 0);
            _comboItemColors[filterDeletions] = System.Drawing.Color.Black;
            _comboItemColors[filterRotations] = System.Drawing.Color.FromArgb(255, 140, 169, 255);
        }

        private void UpdateHelpMenuAvailability()
        {
            string culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName?.ToLowerInvariant() ?? "";
            string instructionPath = Path.Combine(Application.StartupPath, $"UserGuide_{culture}.pdf");
            if (!File.Exists(instructionPath)) instructionPath = Path.Combine(Application.StartupPath, "UserGuide.pdf");
            helpMenuItem.Enabled = File.Exists(instructionPath);
        }

        private void SetLanguage(string cultureName)
        {
            try
            {
                var culture = new CultureInfo(cultureName);
                System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
                System.Threading.Thread.CurrentThread.CurrentCulture = culture;
                Resources.Culture = culture;
                ApplyLocalization();
                UpdateHelpMenuAvailability();
            }
            catch
            {
                // ignore invalid culture
            }
        }

        private void LanguageEnglishToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Properties.Settings.Default.PreferredUICulture = "en";
            Properties.Settings.Default.Save();
            SetLanguage("en");
        }

        private void LanguagePolishToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Properties.Settings.Default.PreferredUICulture = "pl-PL";
            Properties.Settings.Default.Save();
            SetLanguage("pl-PL");
        }

        private void LanguageSystemToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Clear preference to use system culture
            Properties.Settings.Default.PreferredUICulture = string.Empty;
            Properties.Settings.Default.Save();
            var sys = CultureInfo.CurrentUICulture;
            System.Threading.Thread.CurrentThread.CurrentUICulture = sys;
            System.Threading.Thread.CurrentThread.CurrentCulture = sys;
            Resources.Culture = sys;
            ApplyLocalization();
            UpdateHelpMenuAvailability();
        }

        private void ThemeSoftLightMenuItem_Click(object sender, EventArgs e)
        {
            SetTheme(UiThemeKind.SoftLight);
        }

        private void ThemeNordCoolLightMenuItem_Click(object sender, EventArgs e)
        {
            SetTheme(UiThemeKind.NordCoolLight);
        }

        private void ThemeBalticBreezeMenuItem_Click(object sender, EventArgs e)
        {
            SetTheme(UiThemeKind.BalticBreeze);
        }

        private void ThemeWarmSandMenuItem_Click(object sender, EventArgs e)
        {
            SetTheme(UiThemeKind.WarmSand);
        }

        private void ThemeForestGreenMenuItem_Click(object sender, EventArgs e)
        {
            SetTheme(UiThemeKind.ForestGreen);
        }

        private void ThemeGraphiteDarkMenuItem_Click(object sender, EventArgs e)
        {
            SetTheme(UiThemeKind.GraphiteDark);
        }

        private void ThemeOledDarkTealMenuItem_Click(object sender, EventArgs e)
        {
            SetTheme(UiThemeKind.OledDarkTeal);
        }

        private void FilterComboBox_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var combo = (ComboBox)sender;
            string text = combo.Items[e.Index].ToString();

            bool isDropped = combo.DroppedDown;
            bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            // 1) background
            if (isDropped)
            {
                var bg = isSelected
                    ? SystemBrushes.Highlight
                    : new SolidBrush(e.BackColor);
                e.Graphics.FillRectangle(bg, e.Bounds);
            }
            else
            {
                e.Graphics.FillRectangle(new SolidBrush(e.BackColor), e.Bounds);
            }

            // 2) text color – default black, or white (HighlightText) when list is expanded and selected
            System.Drawing.Color fore = (isDropped && isSelected)
                ? SystemColors.HighlightText
                : combo.ForeColor;

            // ADDITIONAL CONDITION: when list is NOT expanded, but element is selected,
            // also use HighlightText color
            if (!isDropped && isSelected)
            {
                fore = SystemColors.HighlightText;
            }

            // 3) "all" items without square
            bool isAllItem = text == allComboItem || text == allCategories;

            const int squareSize = 10;
            const int padding = 4;
            int textX = e.Bounds.Left + padding;

            // 4) draw colored square with black border
            if (isDropped && !isAllItem && _comboItemColors.TryGetValue(text, out var squareColor))
            {
                var squareRect = new Rectangle(
                    textX,
                    e.Bounds.Top + (e.Bounds.Height - squareSize) / 2,
                    squareSize,
                    squareSize
                );
                using (var sqBrush = new SolidBrush(squareColor))
                    e.Graphics.FillRectangle(sqBrush, squareRect);
                using (var borderPen = new Pen(System.Drawing.Color.Black, 1))
                    e.Graphics.DrawRectangle(borderPen, squareRect);

                textX += squareSize + padding;
            }

            // 5) bold text
            using (var font = new Font(e.Font, FontStyle.Bold))
            using (var brush = new SolidBrush(fore))
            {
                float textY = e.Bounds.Top + (e.Bounds.Height - font.Height) / 2f;
                e.Graphics.DrawString(text, font, brush, textX, textY);
            }

            // 6) focus border
            e.DrawFocusRectangle();
        }


        private void MergePdfFiles()
        {
            if (mergeForm == null || mergeForm.IsDisposed)
            {
                mergeForm = new MergeFilesForm();
            }

            if (!mergeForm.Visible)
            {
                mergeForm.Show(this);
            }
            else
            {
                mergeForm.BringToFront();
            }
        }

        private static SizeF GetAnnotationSize(string text, Font font, int rotation)
        {
            if (text == null)
                text = string.Empty;

            string normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
            string[] lines = normalized.Split(new[] { '\n' }, StringSplitOptions.None);
            float maxWidth = 0f;
            float lineHeight;

            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            using (StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
                format.Trimming = StringTrimming.None;
                lineHeight = font.GetHeight(g);
                foreach (string line in lines)
                {
                    SizeF size = g.MeasureString(line, font, int.MaxValue, format);
                    if (size.Width > maxWidth)
                        maxWidth = size.Width;
                }
            }

            float height = Math.Max(lineHeight * Math.Max(lines.Length, 1), lineHeight);
            rotation = NormalizeRotation(rotation);
            if (rotation == 0)
                return new SizeF(maxWidth, height);

            float angle = (float)(rotation * Math.PI / 180.0);
            float cos = Math.Abs((float)Math.Cos(angle));
            float sin = Math.Abs((float)Math.Sin(angle));
            float rotatedWidth = (maxWidth * cos) + (height * sin);
            float rotatedHeight = (maxWidth * sin) + (height * cos);

            return new SizeF(rotatedWidth, rotatedHeight);
        }

        private static PointF GetRotationOffsetForBounds(int rotation, float width, float height)
        {
            rotation = NormalizeRotation(rotation);
            if (rotation == 0)
                return new PointF(0f, 0f);

            float angle = (float)(rotation * Math.PI / 180.0);
            float cos = (float)Math.Cos(angle);
            float sin = (float)Math.Sin(angle);

            PointF[] points =
            {
                new PointF(0f, 0f),
                new PointF(width, 0f),
                new PointF(width, height),
                new PointF(0f, height)
            };

            float minX = float.MaxValue;
            float minY = float.MaxValue;
            foreach (PointF pt in points)
            {
                float x = pt.X * cos - pt.Y * sin;
                float y = pt.X * sin + pt.Y * cos;
                if (x < minX)
                    minX = x;
                if (y < minY)
                    minY = y;
            }

            return new PointF(-minX, -minY);
        }

        private static void ApplyAnnotationFromDialog(TextAnnotation annotation, EditTextDialog dlg)
        {
            SizeF textSize = GetAnnotationSize(dlg.AnnotationText, dlg.AnnotationFont, dlg.AnnotationRotation);
            annotation.AnnotationText = dlg.AnnotationText;
            annotation.AnnotationFont = dlg.AnnotationFont;
            annotation.AnnotationColor = dlg.AnnotationColor;
            annotation.AnnotationAlignment = dlg.AnnotationAlignment;
            annotation.AnnotationRotation = dlg.AnnotationRotation;
            annotation.AnnotationBounds = new RectangleF(
                annotation.AnnotationBounds.X,
                annotation.AnnotationBounds.Y,
                textSize.Width,
                textSize.Height
            );
        }

        private static void LogDebug(string message)
        {
            if (!DebugLogEnabled || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            try
            {
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";
                File.AppendAllText(DebugLogPath, line);
            }
            catch
            {
                // Ignore logging failures.
            }
        }

        private void OpenDiagnosticLogIfEnabled()
        {
            if (!DebugLogEnabled)
            {
                return;
            }

            try
            {
                if (!File.Exists(DebugLogPath))
                {
                    return;
                }

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = $"\"{DebugLogPath}\"",
                    UseShellExecute = false
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                LogDebug($"Open diagnostic log failed: {ex.Message}");
            }
        }

        private void PersistCurrentPageToProjectFile()
        {
            if (numPages <= 0)
                return;

            string projectPath = !string.IsNullOrWhiteSpace(lastSavedProjectName)
                ? lastSavedProjectName
                : inputProjectPath;

            if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
                return;

            try
            {
                string json = File.ReadAllText(projectPath);
                JObject root = JObject.Parse(json);
                int pageToSave = Math.Max(1, Math.Min(currentPage, numPages));
                root["CurrentPage"] = pageToSave;
                File.WriteAllText(projectPath, root.ToString(Formatting.Indented));
            }
            catch
            {
                // Keep closing flow even if project update fails.
            }
        }

        private void AddEditAnnotation(TextAnnotation annotation = null)
        {
            using (EditTextDialog dlg = new EditTextDialog())
            {
                // If we are editing an existing annotation – set default values in the dialog
                if (annotation != null)
                {
                    dlg.AnnotationText = annotation.AnnotationText;
                    dlg.AnnotationFont = annotation.AnnotationFont;
                    dlg.AnnotationColor = annotation.AnnotationColor;
                    dlg.AnnotationAlignment = annotation.AnnotationAlignment;
                    dlg.AnnotationRotation = annotation.AnnotationRotation;
                    dlg.ApplyChanges = () =>
                    {
                        ApplyAnnotationFromDialog(annotation, dlg);
                        pdfViewer.Invalidate();
                        projectWasChangedAfterLastSave = true;
                        saveProjectButton.Enabled = true;
                        saveProjectMenuItem.Enabled = true;
                    };
                }

                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    // Calculate text size using the selected font
                    SizeF textSize = GetAnnotationSize(dlg.AnnotationText, dlg.AnnotationFont, dlg.AnnotationRotation);

                    float boxWidth = textSize.Width;
                    float boxHeight = textSize.Height;

                    if (annotation != null)
                    {
                        // Edit mode – updating existing annotation,
                        // preserving its original position (X, Y) and updating dimensions
                        ApplyAnnotationFromDialog(annotation, dlg);
                    }
                    else
                    {
                        // Add mode – creating new annotation
                        TextAnnotation newAnnotation = new TextAnnotation
                        {
                            PageNumber = currentPage,
                            AnnotationText = dlg.AnnotationText,
                            AnnotationFont = dlg.AnnotationFont,
                            AnnotationColor = dlg.AnnotationColor,
                            AnnotationAlignment = dlg.AnnotationAlignment,
                            AnnotationRotation = dlg.AnnotationRotation
                        };
                        newAnnotation.AnnotationBounds = GetCenteredAnnotationBounds(boxWidth, boxHeight);

                        // Add the new annotation to the list
                        textAnnotations.Add(newAnnotation);

                        

                        PageItemStatus status = allPageStatuses[currentPage - 1];
                        status.HasTextAnnotations = true;

                        if ((string)filterComboBox.SelectedItem == allComboItem)
                        {
                            ListViewItem currentItem = pagesListView.Items[currentPage - 1];
                            UpdateItemTag(currentItem, currentPage, status.HasSelections, status.HasSearchResults, status.MarkedForDeletion, status.HasTextAnnotations);
                            pagesListView.Invalidate(currentItem.Bounds);
                        }
                        else
                        {
                            // rebuild list according to filter
                            ApplyFilter((string)filterComboBox.SelectedItem);
                        }


                    }

                    pdfViewer.Invalidate();

                    projectWasChangedAfterLastSave = true;
                    saveProjectButton.Enabled = true;
                    saveProjectMenuItem.Enabled = true;

                }
            }
        }

        private RectangleF GetCenteredAnnotationBounds(float width, float height)
        {
            ZoomPanel panel = mainAppSplitContainer.Panel2.Controls[0] as ZoomPanel;
            if (panel == null)
            {
                return new RectangleF(0f, 0f, width, height);
            }

            if (scaleFactor <= 0f)
            {
                return new RectangleF(0f, 0f, width, height);
            }

            int panelW = panel.ClientSize.Width;
            int panelH = panel.ClientSize.Height;
            int imgW = pdfViewer.Width;
            int imgH = pdfViewer.Height;
            int viewW = Math.Min(panelW, imgW);
            int viewH = Math.Min(panelH, imgH);
            int scrollX = panel.HorizontalScroll.Value;
            int scrollY = panel.VerticalScroll.Value;
            float docCenterX = (scrollX + viewW / 2f) / scaleFactor;
            float docCenterY = (scrollY + viewH / 2f) / scaleFactor;
            float x = docCenterX - width / 2f;
            float y = docCenterY - height / 2f;

            return new RectangleF(x, y, width, height);
        }

        private void DuplicateAnnotation(TextAnnotation source)
        {
            if (source == null)
                return;

            SizeF textSize = GetAnnotationSize(source.AnnotationText, source.AnnotationFont, source.AnnotationRotation);
            TextAnnotation copy = new TextAnnotation
            {
                PageNumber = currentPage,
                AnnotationText = source.AnnotationText,
                AnnotationFont = source.AnnotationFont,
                AnnotationColor = source.AnnotationColor,
                AnnotationAlignment = source.AnnotationAlignment,
                AnnotationRotation = source.AnnotationRotation,
                AnnotationIsLocked = source.AnnotationIsLocked,
                AnnotationBounds = GetCenteredAnnotationBounds(textSize.Width, textSize.Height)
            };

            textAnnotations.Add(copy);

            PageItemStatus status = allPageStatuses[currentPage - 1];
            status.HasTextAnnotations = true;

            if ((string)filterComboBox.SelectedItem == allComboItem)
            {
                ListViewItem currentItem = pagesListView.Items[currentPage - 1];
                UpdateItemTag(currentItem, currentPage, status.HasSelections, status.HasSearchResults, status.MarkedForDeletion, status.HasTextAnnotations);
                pagesListView.Invalidate(currentItem.Bounds);
            }
            else
            {
                ApplyFilter((string)filterComboBox.SelectedItem);
            }

            pdfViewer.Invalidate();
            projectWasChangedAfterLastSave = true;
            saveProjectButton.Enabled = true;
            saveProjectMenuItem.Enabled = true;
        }

        public bool PageHasVectorDrawing(iText.Kernel.Pdf.PdfPage page)
        {
            var contentBytes = page.GetContentBytes();
            var contentString = System.Text.Encoding.ASCII.GetString(contentBytes);

            // List of common vector graphics operators
            string[] vectorOperators = { " m ", " l ", " c ", " v ", " y ", " h ", " S ", " s ", " f ", " F ", " f*", " re " };

            foreach (var op in vectorOperators)
            {
                if (contentString.Contains(op))
                    return true;
            }
            return false;
        }

        public void ExportAllImagesToSourceFolder(string pdfPath)
        {
            string pdfFileName = Path.GetFileNameWithoutExtension(pdfPath);
            string pdfDirectory = Path.GetDirectoryName(pdfPath);
            string outputDir = Path.Combine(pdfDirectory, pdfFileName);

            Directory.CreateDirectory(outputDir);
            var props = new ReaderProperties();          
            if (!string.IsNullOrEmpty(userPassword))
            {
                props.SetPassword(System.Text.Encoding.UTF8.GetBytes(userPassword));
            }
            using (var pdfReader = new PdfReader(pdfPath, props).SetUnethicalReading(Properties.Settings.Default.IgnorePdfRestrictions))
            using (var pdfDoc = new iText.Kernel.Pdf.PdfDocument(pdfReader))
            {
                int imgIndex = 1;

                for (int pageNum = 1; pageNum <= pdfDoc.GetNumberOfPages(); pageNum++)
                {
                    var page = pdfDoc.GetPage(pageNum);
                    var resources = page.GetResources();
                    var xObjects = resources.GetResource(PdfName.XObject);

                    bool hasVector = PageHasVectorDrawing(page);
                    if (hasVector)
                    {
                        // Rasterize the entire page to JPG (PDFiumSharp) – as you have:
                        using (var pdfDocSharp = new PDFiumSharp.PdfDocument(pdfPath, userPassword))
                        using (var pageSharp = pdfDocSharp.Pages[pageNum - 1])
                        {
                            int dpi = 300;
                            int widthPx = (int)(pageSharp.Width * dpi / 72.0);
                            int heightPx = (int)(pageSharp.Height * dpi / 72.0);

                            using (PDFiumBitmap pdfiumBmp = new PDFiumBitmap(widthPx, heightPx, false))
                            {
                                pdfiumBmp.FillRectangle(0, 0, widthPx, heightPx, 0xFFFFFFFF);
                                pageSharp.Render(renderTarget: pdfiumBmp, flags: RenderingFlags.Annotations);

                                var image = System.Drawing.Image.FromStream(pdfiumBmp.AsBmpStream());
                                image.Save(Path.Combine(outputDir, $"img_{pageNum}.jpg"), System.Drawing.Imaging.ImageFormat.Jpeg);
                            }
                        }
                        imgIndex++;
                        continue;
                    }

                    if (xObjects != null)
                    {
                        foreach (var xObjName in xObjects.KeySet())
                        {
                            var xObjStream = xObjects.GetAsStream(xObjName);
                            if (xObjStream == null) continue;
                            var subtype = xObjStream.GetAsName(PdfName.Subtype);
                            if (subtype == null || !subtype.Equals(PdfName.Image)) continue;

                            // Read filters (list or single)
                            var filterObj = xObjStream.Get(PdfName.Filter);
                            List<string> filters = new List<string>();
                            if (filterObj is PdfName fName)
                            {
                                filters.Add(fName.GetValue());
                            }
                            else if (filterObj is PdfArray arr)
                            {
                                foreach (var f in arr)
                                    if (f is PdfName fname) filters.Add(fname.GetValue());
                            }

                            // Get decompressed bytes
                            byte[] imageBytes = xObjStream.GetBytes(true);

                            // Handle JPEG (DCTDecode, even if wrapped in FlateDecode)
                            if (filters.Contains("DCTDecode") || filters.Contains("JPXDecode"))
                            {
                                string ext = filters.Contains("DCTDecode") ? "jpg" : "jpx";
                                string filePath = Path.Combine(outputDir, $"img_{imgIndex}.{ext}");
                                // If final filter is DCTDecode/JPXDecode, imageBytes is decoded JPEG/JPEG2000
                                try
                                {
                                    using (var ms = new MemoryStream(imageBytes))
                                    using (var img = System.Drawing.Image.FromStream(ms))
                                    {
                                        img.Save(Path.Combine(outputDir, $"img_{imgIndex}.png"), System.Drawing.Imaging.ImageFormat.Png);
                                    }
                                }
                                catch
                                {
                                    // If something goes wrong, save as original JPEG
                                    File.WriteAllBytes(filePath, imageBytes);
                                }
                            }
                            else if (filters.Contains("CCITTFaxDecode"))
                            {
                                int width = xObjStream.GetAsInt(PdfName.Width) ?? 0;
                                int height = xObjStream.GetAsInt(PdfName.Height) ?? 0;
                                int bpc = xObjStream.GetAsInt(PdfName.BitsPerComponent) ?? 1;

                                if (bpc == 1 && width > 0 && height > 0)
                                {
                                    Bitmap bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format1bppIndexed);
                                    System.Drawing.Imaging.BitmapData bmpData = bmp.LockBits(
                                        new System.Drawing.Rectangle(0, 0, width, height),
                                        System.Drawing.Imaging.ImageLockMode.WriteOnly,
                                        System.Drawing.Imaging.PixelFormat.Format1bppIndexed
                                    );

                                    int stride = bmpData.Stride;
                                    int bytesPerLine = (width + 7) / 8;

                                    for (int y = 0; y < height; y++)
                                    {
                                        int srcIndex = y * bytesPerLine;
                                        int dstIndex = y * stride;
                                        if (srcIndex + bytesPerLine <= imageBytes.Length)
                                        {
                                            System.Runtime.InteropServices.Marshal.Copy(
                                                imageBytes, srcIndex, bmpData.Scan0 + dstIndex, bytesPerLine
                                            );
                                        }
                                    }

                                    bmp.UnlockBits(bmpData);
                                    bmp.Save(Path.Combine(outputDir, $"img_{imgIndex}.tiff"), System.Drawing.Imaging.ImageFormat.Tiff);
                                    bmp.Dispose();
                                }
                                else
                                {
                                    // Fallback: save raw bytes
                                    File.WriteAllBytes(
                                        Path.Combine(outputDir, $"img_{imgIndex}.bin"),
                                        imageBytes
                                    );
                                }
                            }
                            else
                            {
                                // Other cases, e.g. DeviceRGB, DeviceGray
                                int width = xObjStream.GetAsInt(PdfName.Width) ?? 0;
                                int height = xObjStream.GetAsInt(PdfName.Height) ?? 0;
                                int bpc = xObjStream.GetAsInt(PdfName.BitsPerComponent) ?? 8;

                                PdfObject csObj = xObjStream.Get(PdfName.ColorSpace);
                                string colorSpace = "";
                                if (csObj is PdfName csName)
                                    colorSpace = csName.GetValue();
                                else if (csObj is PdfArray csArr && csArr.Size() > 0 && csArr.Get(0) is PdfName csArrName)
                                    colorSpace = csArrName.GetValue();

                                Bitmap bmp = null;

                                if (colorSpace == "DeviceRGB" && bpc == 8)
                                {
                                    bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                                    System.Drawing.Imaging.BitmapData bmpData = bmp.LockBits(
                                        new System.Drawing.Rectangle(0, 0, width, height),
                                        System.Drawing.Imaging.ImageLockMode.WriteOnly,
                                        System.Drawing.Imaging.PixelFormat.Format24bppRgb
                                    );
                                    int stride = bmpData.Stride;
                                    int expectedBytesPerLine = width * 3;

                                    for (int y = 0; y < height; y++)
                                    {
                                        int srcIndex = y * expectedBytesPerLine;
                                        int dstIndex = y * stride;
                                        if (srcIndex + expectedBytesPerLine <= imageBytes.Length)
                                        {
                                            System.Runtime.InteropServices.Marshal.Copy(
                                                imageBytes, srcIndex, bmpData.Scan0 + dstIndex, expectedBytesPerLine
                                            );
                                        }
                                    }
                                    bmp.UnlockBits(bmpData);
                                }
                                else if (colorSpace == "DeviceGray" && bpc == 8)
                                {
                                    bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format8bppIndexed);
                                    System.Drawing.Imaging.ColorPalette pal = bmp.Palette;
                                    for (int i = 0; i < 256; i++)
                                        pal.Entries[i] = System.Drawing.Color.FromArgb(i, i, i);
                                    bmp.Palette = pal;

                                    System.Drawing.Imaging.BitmapData bmpData = bmp.LockBits(
                                        new System.Drawing.Rectangle(0, 0, width, height),
                                        System.Drawing.Imaging.ImageLockMode.WriteOnly,
                                        System.Drawing.Imaging.PixelFormat.Format8bppIndexed
                                    );
                                    int stride = bmpData.Stride;

                                    for (int y = 0; y < height; y++)
                                    {
                                        int srcIndex = y * width;
                                        int dstIndex = y * stride;
                                        if (srcIndex + width <= imageBytes.Length)
                                        {
                                            System.Runtime.InteropServices.Marshal.Copy(
                                                imageBytes, srcIndex, bmpData.Scan0 + dstIndex, width
                                            );
                                        }
                                    }
                                    bmp.UnlockBits(bmpData);
                                }

                                if (bmp != null)
                                {
                                    bmp.Save(Path.Combine(outputDir, $"img_{imgIndex}.png"), System.Drawing.Imaging.ImageFormat.Png);
                                    bmp.Dispose();
                                }
                                else
                                {
                                    File.WriteAllBytes(
                                        Path.Combine(outputDir, $"img_{imgIndex}.bin"),
                                        imageBytes
                                    );
                                }
                            }
                            imgIndex++;
                        }

                    }
                    
                }
            }
            System.Windows.Forms.MessageBox.Show(this, string.Format(Resources.Msg_ExportGraphics_Done, outputDir), Resources.Title_Success, System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
        }


        private void InitSplitPdf()
        {
            // Display dialog with page range, where maximum range is based on numPages variable.

            using (SplitDocumentDialog dlg = new SplitDocumentDialog(numPages, inputPdfPath))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    Console.WriteLine("OK");
                    if (dlg.SelectedFile != "" && (dlg.PageNumbers.Count > 0 || dlg.Step > 0))
                    {
                        SplitPdf(dlg.SelectedFile, dlg.PageNumbers, dlg.Step);
                    }
                }
            }
        }

        private void SplitPdf(string inputSplitPdfPath, List<int> splitAfterPages, int splitStep)
        {

            string lockedFile = "";
            // first check the file
            try
            {
                using (var reader = new PdfReader(inputSplitPdfPath))
                using (var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader))
                {
                    // try to get page count – this may already throw an exception
                    int pages = pdfDoc.GetNumberOfPages();

                    // try to copy one page to an empty document in memory
                    using (var ms = new MemoryStream())
                    using (var tempWriter = new iText.Kernel.Pdf.PdfWriter(ms))
                    using (var tempDoc = new iText.Kernel.Pdf.PdfDocument(tempWriter))
                    {
                        // this line will throw exception if file has restrictions
                        pdfDoc.CopyPagesTo(1, Math.Min(1, pages), tempDoc);
                    }
                }
            }
            catch (BadPasswordException)
            {
                lockedFile = inputSplitPdfPath;
            }
            catch (Exception ex)
            {
                // if it's another error, also treat it as problem with this PDF
                lockedFile = inputSplitPdfPath + " (error: " + ex.Message + ")";
            }


            if (!string.IsNullOrEmpty(lockedFile))
            {
                // if file is locked, don't proceed
                string msg = $"Cannot split file [{lockedFile}] because it has security settings.\n";
                MessageBox.Show(this, msg, Resources.Title_Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }


            string outputDirectory = Path.GetDirectoryName(inputSplitPdfPath);
            var props = new ReaderProperties();
            if (!string.IsNullOrEmpty(userPassword))
            {
                props.SetPassword(System.Text.Encoding.UTF8.GetBytes(userPassword));
            }
            using (PdfReader reader = new PdfReader(inputSplitPdfPath, props).SetUnethicalReading(Properties.Settings.Default.IgnorePdfRestrictions))
            using (iText.Kernel.Pdf.PdfDocument pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader))
            {
                int totalPages = pdfDoc.GetNumberOfPages();
                // Filter and sort provided numbers, ensuring they are in range [1, totalPages-1]
                List<int> splitPoints = new List<int>();
                if (splitAfterPages.Count > 0)
                {
                    splitPoints = splitAfterPages
                        .Where(p => p >= 1 && p < totalPages)
                        .OrderBy(p => p)
                        .ToList();
                }

                if (splitStep > 0)
                {
                    int step = splitStep;
                    int start = 1;
                    if (splitAfterPages.Count == 1)
                    {
                        start = splitAfterPages.First();
                        step = start + splitStep;
                    }
                    for (int i = start; i <= totalPages; i++)
                    {
                        if (step == i)
                        {
                            if (step < totalPages)
                            {
                                splitPoints.Add(step);
                            }
                            step += splitStep;
                        }
                    }
                }
                // Add the last page as the final split point
                splitPoints.Add(totalPages);
                splitPoints = splitPoints.Distinct().OrderBy(x => x).ToList();

                int startPage = 1;
                int partIndex = 1;
                foreach (int splitPage in splitPoints)
                {
                    string outputFileName = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(inputSplitPdfPath)}_{partIndex}.pdf");
                    using (PdfWriter writer = new PdfWriter(outputFileName))
                    using (iText.Kernel.Pdf.PdfDocument outputDoc = new iText.Kernel.Pdf.PdfDocument(writer))
                    {
                        // Copy pages from startPage to splitPage (inclusive)
                        pdfDoc.CopyPagesTo(startPage, splitPage, outputDoc);
                        ApplyDemoWatermarkIfNeeded(outputDoc);
                    }
                    // The next segment starts after splitPage
                    startPage = splitPage + 1;
                    partIndex++;
                }
            }
            MessageBox.Show(this, Resources.Split_Success, Resources.Title_Success, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }



        private void FilterComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            var selected = (string)filterComboBox.SelectedItem;
            if (selected == allComboItem)
            {
                // restore the full list of pages, without filtering
                pagesListView.BeginUpdate();
                pagesListView.Items.Clear();
                foreach (var status in allPageStatuses)
                {
            var item = new ListViewItem(string.Format(Resources.UI_PageLabelFormat, status.PageNumber)) { Tag = status };
                    pagesListView.Items.Add(item);
                }
                // mark the current page
                var currentItem = pagesListView.Items
                    .Cast<ListViewItem>()
                    .FirstOrDefault(it => ((PageItemStatus)it.Tag).PageNumber == currentPage);
                if (currentItem != null)
                    currentItem.Selected = true;
                pagesListView.EndUpdate();
            }
            else
            {
                // filter and rebuild the list
                ApplyFilter(selected);
            }
        }

        private void ApplyFilter(string filter)
        {
            pagesListView.BeginUpdate();
            pagesListView.Items.Clear();

            // Predicate selecting statuses by filter
            var filterSelections = Resources.UI_Filter_Selections;
            var filterAnnotations = Resources.UI_Filter_Annotations;
            var filterSearches = Resources.UI_Filter_Searches;
            var filterDeletions = Resources.UI_Filter_Deletions;
            var filterRotations = Resources.UI_Filter_Rotations;

            Func<PageItemStatus, bool> predicate = filter switch
            {
                var f when f == filterSelections => s => s.HasSelections,
                var f when f == filterAnnotations => s => s.HasTextAnnotations,
                var f when f == filterSearches => s => s.HasSearchResults,
                var f when f == filterDeletions => s => s.MarkedForDeletion,
                var f when f == filterRotations => s => s.HasRotation,
                _ => s => s.HasSelections || s.HasTextAnnotations || s.HasSearchResults || s.MarkedForDeletion || s.HasRotation
            };

            // Add only those statuses that pass the predicate
            foreach (var status in allPageStatuses.Where(predicate))
            {
                pagesListView.Items.Add(new ListViewItem(string.Format(Resources.UI_PageLabelFormat, status.PageNumber)) { Tag = status });
            }

            // Mark the current page if it is visible
            var currentItem = pagesListView.Items
                .Cast<ListViewItem>()
                .FirstOrDefault(it => ((PageItemStatus)it.Tag).PageNumber == currentPage);
            if (currentItem != null)
                currentItem.Selected = true;

            pagesListView.EndUpdate();
        }


        private void BringAppToFront()
        {
            // If the form is minimized, restore it
            this.BringToFront();
            this.Activate();
            ShowWindow(this.Handle, SW_RESTORE);
            SetForegroundWindow(this.Handle);
        }

        private sealed class RemoteVersionInfo
        {
            public RemoteVersionInfo(string version, string updated, string changesText, string downloadUrl)
            {
                Version = version;
                Updated = updated;
                ChangesText = changesText;
                DownloadUrl = downloadUrl;
            }

            public string Version { get; }
            public string Updated { get; }
            public string ChangesText { get; }
            public string DownloadUrl { get; }
        }

        private RemoteVersionInfo FetchRemoteVersionInfo()
        {
            try
            {
                string baseUrl = LicenseManager.Config?.ServerBaseUrl ?? "https://misart.pl/anonpdfpro";
                string url = baseUrl.TrimEnd('/') + "/version.json";
                var response = VersionHttpClient.GetAsync(url).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var obj = JObject.Parse(json);
                string version = (string)obj["version"];
                string updated = (string)obj["updated"] ?? (string)obj["date"] ?? (string)obj["updatedAt"];

                string changesText = null;
                if (obj["changes"] is JArray changesArray)
                {
                    var lines = changesArray
                        .Select(item => item?.ToString())
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .Select(item => "- " + item)
                        .ToList();
                    if (lines.Count > 0)
                    {
                        changesText = string.Join(Environment.NewLine, lines);
                    }
                }
                else if (obj["changes"] != null)
                {
                    changesText = obj["changes"].ToString();
                }

                string downloadUrl = (string)obj["downloadUrl"];
                return new RemoteVersionInfo(version, updated, changesText, downloadUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error while checking version: " + ex.Message);
                return null;
            }
        }

        private static bool IsRemoteVersionNewer(string remoteVersion, string localVersion)
        {
            if (string.IsNullOrWhiteSpace(remoteVersion))
            {
                return false;
            }

            if (Version.TryParse(remoteVersion, out var remote) && Version.TryParse(localVersion, out var local))
            {
                return remote > local;
            }

            return !string.Equals(remoteVersion, localVersion, StringComparison.OrdinalIgnoreCase);
        }

        private string BuildNewVersionMessage(RemoteVersionInfo info)
        {
            string dateText = string.IsNullOrWhiteSpace(info.Updated) ? "-" : info.Updated;
            string message = string.Format(Resources.Msg_NewVersionAvailable, info.Version, dateText);

            if (!string.IsNullOrWhiteSpace(info.ChangesText))
            {
                message += Environment.NewLine + Environment.NewLine
                    + Resources.Msg_NewVersionChangesHeader + Environment.NewLine
                    + info.ChangesText;
            }
            else
            {
                message += Environment.NewLine + Environment.NewLine + Resources.Msg_NewVersionNoChanges;
            }

            if (!string.IsNullOrWhiteSpace(info.DownloadUrl))
            {
                message += Environment.NewLine + Environment.NewLine
                    + string.Format(Resources.Msg_NewVersionDownload, info.DownloadUrl);
            }

            return message;
        }

        private void CheckForNewVersion()
        {
            var info = FetchRemoteVersionInfo();
            if (info == null || !IsRemoteVersionNewer(info.Version, fileVersion))
            {
                return;
            }

            if (string.Equals(lastNotifiedVersion, info.Version, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            lastNotifiedVersion = info.Version;
            var message = BuildNewVersionMessage(info);

            ShowInfoMessage(message);
        }

        private void RefreshLicenseStatusFromServer()
        {
            LicenseManager.RefreshServerStatus();

            if (InvokeRequired)
            {
                BeginInvoke(new Action(ApplyLicenseStatusUi));
                return;
            }

            ApplyLicenseStatusUi();
        }

        private void ApplyLicenseStatusUi()
        {
            UpdateWindowTitle();
            UpdateSplashLicenseStatus();
            NotifyUpdatesOutOfRangeIfNeeded();
        }

        private void UpdateSplashLicenseStatus()
        {
            if (splashForm == null || splashForm.IsDisposed || splashClosed)
            {
                return;
            }

            splashForm.UpdateLicenseStatus(GetLicenseStatusLine());
            splashForm.UpdateUpdateStatus(GetUpdatesStatusLine());
        }

        // Method called by timer for periodic version checking
        void OnVersionCheck(object sender, EventArgs e)
        {
            CheckForNewVersion();
        }

        private void ShowInfoMessage(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => ShowInfoMessage(message)));
                return;
            }

            MessageBox.Show(this, message, Resources.Title_Info, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void PDFForm_Shown(object sender, EventArgs e)
        {
            if (!Properties.Settings.Default.TutorialShown)
            {
                ShowTutorial();

                // Set flag to true so it doesn't show again
                Properties.Settings.Default.TutorialShown = true;
                Properties.Settings.Default.Save();
            }

            UpdateLeftPanelWidth();
            Task.Run(() => CheckForNewVersion());
            Task.Run(() => RefreshLicenseStatusFromServer());
        }

        private void NotifyUpdatesOutOfRangeIfNeeded()
        {
            if (updatesOutOfRangeNotified)
            {
                return;
            }

            if (!LicenseManager.IsUpdateOutOfRangeForCurrentVersion)
            {
                return;
            }

            updatesOutOfRangeNotified = true;

            if (InvokeRequired)
            {
                BeginInvoke(new Action(NotifyUpdatesOutOfRangeIfNeeded));
                return;
            }

            MessageBox.Show(
                this,
                Resources.Msg_UpdateLicenseOutOfRange,
                Resources.Title_Warning,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        private void ApplyTheme()
        {
            var theme = CurrentTheme;

            this.BackColor = theme.WindowBackColor;
            mainAppSplitContainer.Panel1.BackColor = theme.PanelBackColor;
            mainAppSplitContainer.Panel1.ForeColor = theme.TextPrimaryColor;
            mainAppSplitContainer.Panel2.BackColor = DocumentBackColor;
            pdfViewer.BackColor = DocumentBackColor;

            menuStrip1.BackColor = theme.MenuStripBackColor;
            menuStrip1.ForeColor = theme.TextPrimaryColor;
            menuStrip1.RenderMode = ToolStripRenderMode.Professional;
            menuStrip1.Renderer = new ToolStripProfessionalRenderer(new MenuColorTable(theme))
            {
                RoundedEdges = false
            };
            ApplyMenuItemTheme(menuStrip1.Items, theme);

            ApplyGroupBoxTheme(groupBoxOpen, theme);
            ApplyGroupBoxTheme(groupBoxSave, theme);
            ApplyGroupBoxTheme(groupBoxOptions, theme);
            ApplyGroupBoxTheme(groupBoxPages, theme);
            ApplyGroupBoxTheme(groupBoxSearch, theme);
            ApplyGroupBoxTheme(groupBoxSelections, theme);
            ApplyGroupBoxTheme(groupBoxPagesToRemove, theme);
            ApplyGroupBoxTheme(groupBoxSignatures, theme);
            ApplyGroupBoxTheme(groupBoxFilter, theme);

            pagesListView.BackColor = theme.SectionBackColor;
            pagesListView.ForeColor = theme.TextPrimaryColor;
            tableLayoutPanel1.BackColor = theme.SectionBackColor;

            ApplyThemeToControls(mainAppSplitContainer.Panel1.Controls, theme);
            ApplyThemeToControls(tableLayoutPanel1.Controls, theme);
            ApplyIconButtonTheme(removePageButton, theme);
            ApplyIconButtonTheme(removePageRangeButton, theme);
            ApplyDangerButtonTheme(clearSelectionButton, theme);
            ApplyTitleBarColor();
            UpdateSaveGroupState();
            UpdateOptionsGroupState();
            UpdateRecentFilesMenu();
            UpdateThemeMenuChecks();
        }

        private void LoadPreferredTheme()
        {
            var preferredTheme = Properties.Settings.Default.PreferredUiTheme;
            if (string.IsNullOrWhiteSpace(preferredTheme))
            {
                return;
            }

            if (Enum.TryParse(preferredTheme, out UiThemeKind parsedTheme) && UiThemes.ContainsKey(parsedTheme))
            {
                currentThemeKind = parsedTheme;
            }
        }

        private void SetTheme(UiThemeKind kind)
        {
            if (currentThemeKind == kind)
            {
                UpdateThemeMenuChecks();
                return;
            }

            currentThemeKind = kind;
            Properties.Settings.Default.PreferredUiTheme = kind.ToString();
            Properties.Settings.Default.Save();
            ApplyTheme();
        }

        private void ApplyThemeToControls(Control.ControlCollection controls, UiThemePalette theme)
        {
            foreach (Control control in controls)
            {
                if (control is ThemedCheckBox themedCheckBox)
                {
                    var backColor = control.Parent?.BackColor ?? theme.SectionBackColor;
                    themedCheckBox.BackColor = backColor;
                    themedCheckBox.UseVisualStyleBackColor = false;
                    themedCheckBox.ForeColor = theme.TextPrimaryColor;
                    themedCheckBox.DisabledForeColor = theme.TextSecondaryColor;
                }
                else if (control is ThemedRadioButton themedRadioButton)
                {
                    var backColor = control.Parent?.BackColor ?? theme.SectionBackColor;
                    themedRadioButton.BackColor = backColor;
                    themedRadioButton.UseVisualStyleBackColor = false;
                    themedRadioButton.ForeColor = theme.TextPrimaryColor;
                    themedRadioButton.DisabledForeColor = theme.TextSecondaryColor;
                }
                else if (control is Button button)
                {
                    ApplySecondaryButtonTheme(button, theme);
                }
                else if (control is CheckBox checkBox)
                {
                    checkBox.ForeColor = theme.TextPrimaryColor;
                }
                else if (control is RadioButton radioButton)
                {
                    radioButton.ForeColor = theme.TextPrimaryColor;
                }
                else if (control is Label label)
                {
                    label.ForeColor = theme.TextSecondaryColor;
                }
                else if (control is TextBox textBox)
                {
                    textBox.BackColor = GetInputBackColor(theme);
                    textBox.ForeColor = theme.TextPrimaryColor;
                }
                else if (control is ComboBox comboBox)
                {
                    comboBox.BackColor = GetInputBackColor(theme);
                    comboBox.ForeColor = theme.TextPrimaryColor;
                }
                else if (control is ListView listView)
                {
                    listView.BackColor = listView == pagesListView
                        ? theme.SectionBackColor
                        : theme.PanelBackColor;
                    listView.ForeColor = theme.TextPrimaryColor;
                }
                else if (control is TableLayoutPanel table)
                {
                    table.BackColor = table == tableLayoutPanel1
                        ? theme.SectionBackColor
                        : theme.PanelBackColor;
                }

                if (control.Controls.Count > 0)
                {
                    ApplyThemeToControls(control.Controls, theme);
                }
            }
        }

        private System.Drawing.Color GetInputBackColor(UiThemePalette theme)
        {
            return currentThemeKind == UiThemeKind.BalticBreeze
                ? System.Drawing.Color.White
                : theme.PanelBackColor;
        }

        private static void ApplyIconButtonTheme(Button button, UiThemePalette theme)
        {
            if (button == null)
            {
                return;
            }

            button.BackColor = theme.SecondaryButtonBackColor;
            button.ForeColor = button.Enabled ? theme.TextPrimaryColor : theme.TextSecondaryColor;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = theme.BorderColor;
            button.UseVisualStyleBackColor = false;
        }

        private void ApplySecondaryButtonTheme(Button button, UiThemePalette theme)
        {
            if (button == null || button == clearSelectionButton || button == removePageButton || button == removePageRangeButton)
            {
                return;
            }

            button.BackColor = theme.SecondaryButtonBackColor;
            button.ForeColor = button.Enabled ? theme.SecondaryButtonForeColor : theme.TextSecondaryColor;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = theme.BorderColor;
            button.UseVisualStyleBackColor = false;
        }

        private void UpdateThemeMenuChecks()
        {
            if (themeSoftLightMenuItem == null)
            {
                return;
            }

            themeSoftLightMenuItem.Checked = currentThemeKind == UiThemeKind.SoftLight;
            themeNordCoolLightMenuItem.Checked = currentThemeKind == UiThemeKind.NordCoolLight;
            themeBalticBreezeMenuItem.Checked = currentThemeKind == UiThemeKind.BalticBreeze;
            themeWarmSandMenuItem.Checked = currentThemeKind == UiThemeKind.WarmSand;
            themeForestGreenMenuItem.Checked = currentThemeKind == UiThemeKind.ForestGreen;
            themeGraphiteDarkMenuItem.Checked = currentThemeKind == UiThemeKind.GraphiteDark;
            themeOledDarkTealMenuItem.Checked = currentThemeKind == UiThemeKind.OledDarkTeal;
        }

        private static void ApplyMenuItemTheme(ToolStripItemCollection items, UiThemePalette theme)
        {
            foreach (ToolStripItem item in items)
            {
                item.ForeColor = theme.TextPrimaryColor;
                if (item is ToolStripDropDownItem dropDownItem && dropDownItem.DropDownItems.Count > 0)
                {
                    ApplyMenuItemTheme(dropDownItem.DropDownItems, theme);
                }
            }
        }

        private static void ApplyGroupBoxTheme(GroupBox groupBox, UiThemePalette theme)
        {
            if (groupBox == null)
            {
                return;
            }

            groupBox.BackColor = theme.SectionBackColor;
            if (groupBox is ThemedGroupBox themedGroupBox)
            {
                themedGroupBox.ForeColor = theme.TextPrimaryColor;
                themedGroupBox.DisabledForeColor = theme.TextSecondaryColor;
                themedGroupBox.Invalidate();
            }
            else
            {
                groupBox.ForeColor = groupBox.Enabled ? theme.TextPrimaryColor : theme.TextSecondaryColor;
            }
        }

        private void UpdateRemovePageButtonVisual(bool isMarked)
        {
            if (removePageButton == null)
            {
                return;
            }

            if (isMarked)
            {
                removePageButton.BackColor = System.Drawing.Color.Black;
                removePageButton.ForeColor = System.Drawing.Color.White;
            }
            else
            {
                removePageButton.BackColor = CurrentTheme.SecondaryButtonBackColor;
                removePageButton.ForeColor = CurrentTheme.SecondaryButtonForeColor;
            }
        }

        private static void ApplyDangerButtonTheme(Button button, UiThemePalette theme)
        {
            if (button == null)
            {
                return;
            }

            button.BackColor = theme.DangerBackColor;
            button.ForeColor = System.Drawing.Color.White;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = theme.BorderColor;
            button.UseVisualStyleBackColor = false;
        }

        private void ApplyTitleBarColor()
        {
            if (DesignMode || !IsHandleCreated)
            {
                return;
            }

            try
            {
                int colorRef = System.Drawing.ColorTranslator.ToWin32(CurrentTheme.WindowBackColor);
                DwmSetWindowAttribute(this.Handle, DWMWA_CAPTION_COLOR, ref colorRef, sizeof(int));
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        private void UpdateLeftPanelWidth()
        {
            int extra = mainAppSplitContainer.Panel1.VerticalScroll.Visible
                ? SystemInformation.VerticalScrollBarWidth + LeftPanelScrollbarPadding
                : 0;
            int desiredWidth = LeftPanelBaseWidth + extra;
            if (mainAppSplitContainer.SplitterDistance != desiredWidth)
            {
                mainAppSplitContainer.SplitterDistance = desiredWidth;
            }
        }

        private void UpdateSaveGroupState()
        {
            if (groupBoxSave == null)
            {
                return;
            }

            groupBoxSave.Enabled = saveProjectButton.Enabled
                || buttonRedactText.Enabled
                || savePdfMenuItem.Enabled
                || (saveProjectAsButton.Visible && saveProjectAsButton.Enabled);
            groupBoxSave.ForeColor = groupBoxSave.Enabled ? CurrentTheme.TextPrimaryColor : CurrentTheme.TextSecondaryColor;
        }

        private void UpdateOptionsGroupState()
        {
            if (groupBoxOptions == null)
            {
                return;
            }

            bool optionsAvailable = pdf != null;
            groupBoxOptions.Enabled = true;
            groupBoxOptions.ForeColor = optionsAvailable ? CurrentTheme.TextPrimaryColor : CurrentTheme.TextSecondaryColor;
        }

        private void PDFForm_Load(object sender, EventArgs e)
        {
            // Replace Panel2 with a ZoomPanel wrapper
            ZoomPanel zoomPanel = new ZoomPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Visible = false,
            };
            

            // Move all existing controls from Panel2 into ZoomPanel
            while (mainAppSplitContainer.Panel2.Controls.Count > 0)
            {
                Control ctrl = mainAppSplitContainer.Panel2.Controls[0];
                mainAppSplitContainer.Panel2.Controls.RemoveAt(0);
                zoomPanel.Controls.Add(ctrl);
            }

            // Attach the ZoomPanel to Panel2
            mainAppSplitContainer.Panel2.Controls.Add(zoomPanel);
            zoomPanel.Visible = true;

            // If the tutorial binary is missing, disable the menu entry
            string tutorialPath = Path.Combine(Application.StartupPath, $"{tutorialProcessName}.exe");
            if (!File.Exists(tutorialPath))
            {
                tutorialMenuItem.Enabled = false;
            }

            // If the help PDF is missing for current culture and fallback, disable the menu entry
            string culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName?.ToLowerInvariant() ?? "";
            string instructionPath = Path.Combine(Application.StartupPath, $"UserGuide_{culture}.pdf");
            if (!File.Exists(instructionPath)) instructionPath = Path.Combine(Application.StartupPath, "UserGuide.pdf");
            if (!File.Exists(instructionPath))
            {
                helpMenuItem.Enabled = false;
            }

            // Load setting "Ignore PDF restrictions"
            ignorePdfRestrictionsToolStripMenuItem.Checked = Properties.Settings.Default.IgnorePdfRestrictions;
            diagnosticModeEnabled = false;
            diagnosticModeMenuItem.Checked = false;
            UpdateRecentFilesMenu();

            if (DebugLogEnabled)
            {
                LogDebug("=== Session start ===");
            }
        }

        private void IgnorePdfRestrictionsToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            // Zapisz ustawienie
            Properties.Settings.Default.IgnorePdfRestrictions = ignorePdfRestrictionsToolStripMenuItem.Checked;
            Properties.Settings.Default.Save();
        }

        private void FullScreenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetFullScreen(!isFullScreen);
        }

        private void SetFullScreen(bool enable)
        {
            if (enable == isFullScreen)
            {
                return;
            }

            if (enable)
            {
                previousFormBorderStyle = FormBorderStyle;
                previousWindowState = WindowState;
                previousBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                previousTopMost = TopMost;

                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Normal;
                Bounds = Screen.FromControl(this).Bounds;
                TopMost = true;
            }
            else
            {
                FormBorderStyle = previousFormBorderStyle;
                WindowState = FormWindowState.Normal;
                Bounds = previousBounds;
                TopMost = previousTopMost;
                if (previousWindowState != FormWindowState.Normal)
                {
                    WindowState = previousWindowState;
                }
            }

            isFullScreen = enable;
            if (fullScreenToolStripMenuItem != null)
            {
                fullScreenToolStripMenuItem.Checked = isFullScreen;
            }
        }

        private void DiagnosticModeMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            diagnosticModeEnabled = diagnosticModeMenuItem.Checked;

            if (diagnosticModeMenuItem.Checked)
            {
                LogDebug("=== Diagnostic mode enabled ===");
            }
        }

        private void ShowTutorial()
        {
            string tutorialPath = Path.Combine(Application.StartupPath, $"{tutorialProcessName}.exe");
            if (!File.Exists(tutorialPath))
            {
                MessageBox.Show(this, string.Format(Resources.Tutorial_NotFound, tutorialPath),
                                Resources.Title_Error,
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
                return;
            }
            try
            {
                Process[] processes = Process.GetProcessesByName(tutorialProcessName);
                Process tutorialProcess;
                if (processes.Any())
                {
                    // If process is already running, restore window
                    tutorialProcess = processes.First();
                    IntPtr hWnd = tutorialProcess.MainWindowHandle;
                    if (hWnd != IntPtr.Zero)
                    {
                        if (IsIconic(hWnd))
                        {
                            ShowWindow(hWnd, SW_RESTORE);
                        }
                        SetForegroundWindow(hWnd);
                    }
                }
                else
                {
                    tutorialProcess = new Process();
                    tutorialProcess.StartInfo.FileName = tutorialPath;
                    tutorialProcess.StartInfo.WorkingDirectory = Path.GetDirectoryName(tutorialPath);
                    tutorialProcess.Start();

                    // Launch tutorial in modal mode (blocks main application window)
                    if (!Properties.Settings.Default.TutorialShown)
                    {
                        tutorialProcess.WaitForExit();
                    }
                }



            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(Resources.Tutorial_OpenError, ex.Message), Resources.Title_Error,
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateItemTag(ListViewItem item, int pageNumber, bool hasSelections, bool hasSearchResults, bool markedForDeletion, bool hasTextAnnotations = false)
        {
            bool hasRotation = pageRotationOffsets.ContainsKey(pageNumber);
            // Create new PageItemStatus object with new values
            PageItemStatus newStatus = new PageItemStatus()
            {
                PageNumber = pageNumber,
                HasSelections = hasSelections,
                HasSearchResults = hasSearchResults,
                MarkedForDeletion = markedForDeletion,
                HasTextAnnotations = hasTextAnnotations,
                HasRotation = hasRotation
            };

            // Assign new object to Tag property
            item.Tag = newStatus;

            // Refresh element
            item.ListView?.Invalidate(item.Bounds);

        }

        private void PagesListView_DrawItem(object sender, DrawListViewItemEventArgs e)
        {
            if (e.Item.Tag is PageItemStatus status)
            {
                e.DrawBackground();

                // Set the dimensions of the rectangles
                int rectangleWidth = 10;                       // rectangle width
                int x = e.Bounds.Left + 2;        // margines od lewej
                int offset = 0;                        // how much we have drawn from the bottom

                // Array of flags with corresponding colors, in drawing order
                var statusColors = new (bool Flag, System.Drawing.Color Color)[]
                {
                    (status.HasSelections,     System.Drawing.Color.Red),
                    (status.HasSearchResults,  System.Drawing.Color.FromArgb(255, 255, 215, 0)),
                    (status.MarkedForDeletion, System.Drawing.Color.Black),
                    (status.HasRotation,        System.Drawing.Color.FromArgb(255, 140, 169, 255)),
                    (status.HasTextAnnotations, System.Drawing.Color.Green)
                };
                int gap = 1;
                int rectangleHeight = Math.Max(2, (e.Bounds.Height - (statusColors.Length - 1) * gap) / statusColors.Length);

                // Draw only those rectangles whose flag is true,
                // each subsequent one "from bottom" shifted by rectangleHeight
                foreach (var item in statusColors)
                {
                    if (!item.Flag)
                        continue;

                    int y = e.Bounds.Bottom - rectangleHeight - offset * (rectangleHeight + gap);
                    var rect = new DrawingRectangle(x, y, rectangleWidth, rectangleHeight);

                    using (var brush = new SolidBrush(item.Color))
                        e.Graphics.FillRectangle(brush, rect);

                    offset++;
                }

                Size textSize = TextRenderer.MeasureText(e.Item.Text, e.Item.Font);
                DrawingRectangle textRect = new DrawingRectangle(e.Bounds.Left + rectangleWidth + 4, e.Bounds.Top, textSize.Width, e.Bounds.Height);

                // For current page use system highlight colors
                if (status.PageNumber == currentPage)
                {
                    using (SolidBrush highlightBrush = new SolidBrush(SystemColors.Highlight))
                    {
                        e.Graphics.FillRectangle(highlightBrush, textRect);
                    }
                    TextRenderer.DrawText(e.Graphics, e.Item.Text, e.Item.Font, textRect, SystemColors.HighlightText, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                }
                else
                {
                    // Normal text drawing for remaining elements
                    TextRenderer.DrawText(e.Graphics, e.Item.Text, e.Item.Font, textRect, e.Item.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                }

            }
            else
            {
                e.DrawDefault = true;
            }
        }


        public bool CurrentPageContainsText()
        {
            // Ensure that currentPage and inputPdfPath are correctly set
            var props = new ReaderProperties();
            if (!string.IsNullOrEmpty(userPassword))
            {
                props.SetPassword(System.Text.Encoding.UTF8.GetBytes(userPassword));
            }
            using (PdfReader reader = new PdfReader(inputPdfPath, props).SetUnethicalReading(Properties.Settings.Default.IgnorePdfRestrictions))
            using (iText.Kernel.Pdf.PdfDocument pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader))
            {
                var page = pdfDoc.GetPage(currentPage);
                string extractedText = PdfTextExtractor.GetTextFromPage(page);
                return !string.IsNullOrWhiteSpace(extractedText);
            }
        }

        public bool PdfDocumentContainsText()
        {
            var props = new ReaderProperties();
            if (!string.IsNullOrEmpty(userPassword))
            {
                props.SetPassword(System.Text.Encoding.UTF8.GetBytes(userPassword));
            }
            using (PdfReader reader = new PdfReader(inputPdfPath, props).SetUnethicalReading(Properties.Settings.Default.IgnorePdfRestrictions))
            using (iText.Kernel.Pdf.PdfDocument pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader))
            {
                for (int i = 1; i <= numPages; i++)
                {
                    var page = pdfDoc.GetPage(i);
                    string extractedText = PdfTextExtractor.GetTextFromPage(page);
                    if (!string.IsNullOrWhiteSpace(extractedText))
                        return true;
                }
                return false;
            }
        }


        private void RenderTimer_Tick(object sender, EventArgs e)
        {
            renderTimer.Stop();
            ShowRedactPreview();
            pdfViewer.Invalidate();
        }

        private void PagingTimer_Tick(object sender, EventArgs e)
        {
            pagingTimer.Stop();
            this.Cursor = Cursors.WaitCursor;
            DisplayPdfPage(currentPage);
            this.Cursor = Cursors.Default;
        }

        private void ZoomTimer_Tick(object sender, EventArgs e)
        {
            zoomTimer.Stop();

            if (zoomPending)
            {
                // Set global scaleFactor to last calculated value
                scaleFactor = pendingScaleFactor;

                // Render page using new scale.
                // DisplayPdfPage should set pdfViewer.Image and pdfViewer.Size according to scaleFactor.
                DisplayPdfPage(currentPage);

                // Set scrollbars so that the document point (pendingDocCoordX, pendingDocCoordY)
                // ends up in the center of the visible area.
                ZoomPanel panel = mainAppSplitContainer.Panel2.Controls[0] as ZoomPanel;
                if (panel is ZoomPanel)
                {
                    int newScrollX = (int)(pendingDocCoordX * scaleFactor) - (panel.ClientSize.Width / 2);
                    int newScrollY = (int)(pendingDocCoordY * scaleFactor) - (panel.ClientSize.Height / 2);

                    // Correction – if new values go beyond scroll range
                    if (newScrollX < panel.HorizontalScroll.Minimum)
                        newScrollX = panel.HorizontalScroll.Minimum;
                    if (newScrollX > panel.HorizontalScroll.Maximum)
                        newScrollX = panel.HorizontalScroll.Maximum;
                    if (newScrollY < panel.VerticalScroll.Minimum)
                        newScrollY = panel.VerticalScroll.Minimum;
                    if (newScrollY > panel.VerticalScroll.Maximum)
                        newScrollY = panel.VerticalScroll.Maximum;

                    panel.HorizontalScroll.Value = newScrollX;
                    panel.VerticalScroll.Value = newScrollY;
                    panel.PerformLayout();
                    panel.Refresh();
                }

                zoomPending = false;
                this.Cursor = Cursors.Default;
            }
        }

        private void PDFForm_DragEnter(object sender, DragEventArgs e)
        {
            // Czy zawiera pliki?
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                // Allow drop
                e.Effect = DragDropEffects.Copy;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }


        private void PDFForm_DragDrop(object sender, DragEventArgs e)
        {
            // Get list of file paths
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files == null || files.Length == 0)
                return;

            // Take first file (or handle more if you want)
            string droppedFile = files[0];
            string extension = Path.GetExtension(droppedFile).ToLowerInvariant();

            // Handle depending on extension
            if (extension == ".pdf")
            {
                // 1) Remember in 'inputPdfPath' field
                inputPdfPath = droppedFile;
                Properties.Settings.Default.LastPdfPath = inputPdfPath;
                Properties.Settings.Default.Save(); // Zapis do pliku user.config

                // 2) Load PDF (e.g. your method that you already have in code)
                LoadPdf();
            }
            else if (extension == ".pap")
            {
                // 2) Load project (e.g. your method to load .pap)
                //    NOTE: for this to make sense, PDF should also be already loaded
                //    or .pap knows which PDF to work with.

                LoadRedactionBlocks(droppedFile);
            }
            else
            {
                MessageBox.Show(this,
                    Resources.Msg_DragDrop_UnsupportedFileType,
                    Resources.Title_Warning,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Exclamation
                );
            }
        }

        public void Panel2_MouseWheel(MouseEventArgs e)
        {
            // If PDF is not loaded, do nothing.
            if (pdf == null) return;

            CalculateMinScaleFactor(currentPage);
            CalculateMaxScaleFactor();

            // (A) Get panel (our ZoomPanel) and cursor position in its coordinate system
            ZoomPanel panel = mainAppSplitContainer.Panel2.Controls[0] as ZoomPanel;
            if (!(panel is ZoomPanel)) return;

            bool rollUp = (e.Delta > 0);

            // If CTRL is pressed – perform zoom
            if ((Control.ModifierKeys & Keys.Control) == Keys.Control)
            {
                // (D) Calculate new scale depending on wheel direction

                if (!zoomPending)
                {
                    this.Cursor = Cursors.WaitCursor;
                    Point mousePosInPanel = panel.PointToClient(Cursor.Position);

                    // (B) Calculate absolute cursor position in content (considering scroll)
                    int screenX = panel.HorizontalScroll.Value + mousePosInPanel.X;
                    int screenY = panel.VerticalScroll.Value + mousePosInPanel.Y;

                    // (C) Convert to document coordinates – i.e. divide by current scaleFactor
                    float docCoordX = screenX / scaleFactor;
                    float docCoordY = screenY / scaleFactor;
                    pendingDocCoordX = docCoordX;
                    pendingDocCoordY = docCoordY;
                    pendingMousePosInPanel = mousePosInPanel;
                }

                float newScale = pendingScaleFactor;
                if (rollUp)
                    newScale += percentScaleFactor;
                else
                    newScale -= percentScaleFactor;

                // Ensure new scale doesn't go beyond established range
                if ((newScale < minScaleFactor))
                {
                    minScaleButton = true;
                    maxScaleButton = false;
                    newScale = minScaleFactor;
                }
                else
                {
                    minScaleButton = false;
                }
                if ((newScale > maxScaleFactor))
                {
                    minScaleButton = false;
                    maxScaleButton = true;
                    newScale = maxScaleFactor;
                }
                else
                {
                    maxScaleButton = false;
                }


                // (E) Save calculated values to pending variables – don't render immediately
                pendingScaleFactor = newScale;

                zoomPending = true;
            

                // (F) Restart timer – if subsequent events appear quickly, timer will restart
                zoomTimer.Stop();
                zoomTimer.Start();

                // Finish – don't execute default rendering
                return;
            }
            else
            {
                int newScrollValue = panel.VerticalScroll.Value;
                bool isVerticalScrollbarVisible = panel.VerticalScroll.Visible;

                if (newScrollValue == oldScrollValue)
                {

                    if (isVerticalScrollbarVisible)
                    {
                        if (wheelResistanceCurentValue < wheelResistanceMaxValue)
                        {
                            wheelResistanceCurentValue++;
                            return;
                        }
                    }
                    if (rollUp)
                    {
                        PreviousPage();
                    }
                    else
                    {
                        NextPage();
                    }
                    wheelResistanceCurentValue = 0;
                }
                oldScrollValue = newScrollValue;
            }
        }

        private static int NormalizeRotation(int rotation)
        {
            rotation %= 360;
            if (rotation < 0)
                rotation += 360;
            return rotation;
        }

        private static int OrientationToDegrees(PageOrientations orientation)
        {
            switch (orientation)
            {
                case PageOrientations.Rotated90CW:
                    return 90;
                case PageOrientations.Rotated180:
                    return 180;
                case PageOrientations.Rotated90CCW:
                    return 270;
                default:
                    return 0;
            }
        }

        private int GetRotationOffset(int pageNumber)
        {
            return pageRotationOffsets.TryGetValue(pageNumber, out int offset) ? offset : 0;
        }

        private void SetRotationOffset(int pageNumber, int offset)
        {
            offset = NormalizeRotation(offset);
            if (offset == 0)
            {
                pageRotationOffsets.Remove(pageNumber);
            }
            else
            {
                pageRotationOffsets[pageNumber] = offset;
            }
        }

        private int GetBaseRotationDegrees(int pageNumber)
        {
            if (pdf == null || pageNumber < 1 || pageNumber > pdf.Pages.Count)
                return 0;

            return OrientationToDegrees(pdf.Pages[pageNumber - 1].Orientation);
        }

        private int GetEffectiveRotationDegrees(int pageNumber)
        {
            int baseRotation = GetBaseRotationDegrees(pageNumber);
            int offset = GetRotationOffset(pageNumber);
            return NormalizeRotation(baseRotation + offset);
        }

        private (float Width, float Height) GetPageSizeWithOffset(int pageNumber)
        {
            if (pdf == null || pageNumber < 1 || pageNumber > pdf.Pages.Count)
                return (0f, 0f);

            var page = pdf.Pages[pageNumber - 1];
            float width = (float)page.Width;
            float height = (float)page.Height;
            int offset = GetRotationOffset(pageNumber);

            if (offset == 90 || offset == 270)
            {
                float tmp = width;
                width = height;
                height = tmp;
            }

            return (width, height);
        }

        private static RectangleF RotateRectClockwise(RectangleF rect, float pageWidth, float pageHeight)
        {
            return new RectangleF(
                pageHeight - rect.Y - rect.Height,
                rect.X,
                rect.Height,
                rect.Width);
        }

        private static RotateFlipType GetRotateFlipForOffset(int offset)
        {
            switch (offset)
            {
                case 90:
                    return RotateFlipType.Rotate90FlipNone;
                case 180:
                    return RotateFlipType.Rotate180FlipNone;
                case 270:
                    return RotateFlipType.Rotate270FlipNone;
                default:
                    return RotateFlipType.RotateNoneFlipNone;
            }
        }

        private static void ApplyRotationOffset(DrawingImage image, int offset)
        {
            var rotateFlip = GetRotateFlipForOffset(offset);
            if (rotateFlip != RotateFlipType.RotateNoneFlipNone)
            {
                image.RotateFlip(rotateFlip);
            }
        }

        private RectangleF ConvertToPdfCoordinates(RectangleF screenRect, int pageNumber, int rotation, bool includeBaseRotation = true)
        {
            // Read page via PDFiumSharp (view space)
            var page = pdf.Pages[pageNumber - 1];
            var viewSize = GetPageSizeWithOffset(pageNumber);
            float viewW = viewSize.Width;
            float viewH = viewSize.Height;

            // Unrotated PDF user space size
            float pageW = (float)page.Width;
            float pageH = (float)page.Height;
            int baseRotation = includeBaseRotation ? GetBaseRotationDegrees(pageNumber) : 0;
            if (baseRotation == 90 || baseRotation == 270)
            {
                float tmp = pageW;
                pageW = pageH;
                pageH = tmp;
            }

            // Rectangle selected by user (screen coords/upper corner)
            float oldX = screenRect.X;
            float oldY = screenRect.Y;
            float oldW = screenRect.Width;
            float oldH = screenRect.Height;

            // Convert to view coords with origin bottom-left
            float xr = oldX;
            float yr = viewH - oldY - oldH;
            float wr = oldW;
            float hr = oldH;

            float x, y, w, h;

            rotation = NormalizeRotation(rotation);
            switch (rotation)
            {
                case 90:
                    x = pageW - (yr + hr);
                    y = xr;
                    w = hr;
                    h = wr;
                    break;

                case 180:
                    x = pageW - (xr + wr);
                    y = pageH - (yr + hr);
                    w = wr;
                    h = hr;
                    break;

                case 270:
                    x = yr;
                    y = pageH - (xr + wr);
                    w = hr;
                    h = wr;
                    break;

                case 0:
                default:
                    x = xr;
                    y = yr;
                    w = wr;
                    h = hr;
                    break;
            }

            return new RectangleF(x, y, w, h);
        }

        private PointF ConvertPointToPdfCoordinates(PointF screenPoint, int pageNumber, int rotation, bool includeBaseRotation = true)
        {
            RectangleF rect = new RectangleF(screenPoint.X, screenPoint.Y, 0f, 0f);
            RectangleF pdfRect = ConvertToPdfCoordinates(rect, pageNumber, rotation, includeBaseRotation);
            return new PointF(pdfRect.X, pdfRect.Y);
        }

        private void ApplyRotationOffsetsToDocument(iText.Kernel.Pdf.PdfDocument pdfDoc, ISet<int> pagesWithBakedRotation = null)
        {
            if (pdf == null)
                return;

            int pageCount = pdfDoc.GetNumberOfPages();
            for (int pageNum = 1; pageNum <= pageCount; pageNum++)
            {
                bool includeBaseRotation = pagesWithBakedRotation == null || !pagesWithBakedRotation.Contains(pageNum);
                int rotation = includeBaseRotation
                    ? GetEffectiveRotationDegrees(pageNum)
                    : NormalizeRotation(GetRotationOffset(pageNum));
                if (rotation != pdfDoc.GetPage(pageNum).GetRotation())
                {
                    pdfDoc.GetPage(pageNum).SetRotation(rotation);
                }
            }
        }

        private DrawingImage RenderOriginalPage(int pageNumber)
        {

            var page = pdf.Pages[pageNumber - 1]; // 'pdf' is PDFiumSharp.PdfDocument loaded original
            int offset = GetRotationOffset(pageNumber);

            using (var bmp = new PDFiumBitmap(
                (int)(page.Width * scaleFactor),
                (int)(page.Height * scaleFactor),
                true))
            {

                bmp.FillRectangle(
                    0, 0,
                    (int)(page.Width * scaleFactor),
                    (int)(page.Height * scaleFactor), 0xFFFFFFFF);

                page.Render(renderTarget: bmp, flags: RenderingFlags.Annotations);

                using (MemoryStream ms = new MemoryStream())
                {
                    bmp.Save(ms);
                    ms.Position = 0;
                    var image = DrawingImage.FromStream(ms);
                    ApplyRotationOffset(image, offset);
                    return image;
                }
            }
        }

        private bool IsNearlyWhite(byte r, byte g, byte b, int tolerance = 10)
        {
            return Math.Abs(r - 255) <= tolerance &&
                   Math.Abs(g - 255) <= tolerance &&
                   Math.Abs(b - 255) <= tolerance;
        }


        private Bitmap CombineBitmaps(Bitmap originalBmp, Bitmap overlayBmp)
        {
            if (originalBmp.Width != overlayBmp.Width || originalBmp.Height != overlayBmp.Height)
                throw new ArgumentException("Both bitmaps must have the same dimensions.");

            DrawingRectangle rect = new DrawingRectangle(0, 0, originalBmp.Width, originalBmp.Height);
            Bitmap resultBmp = new Bitmap(originalBmp.Width, originalBmp.Height, originalBmp.PixelFormat);

            BitmapData dataOrig = originalBmp.LockBits(rect, ImageLockMode.ReadOnly, originalBmp.PixelFormat);
            BitmapData dataOverlay = overlayBmp.LockBits(rect, ImageLockMode.ReadOnly, overlayBmp.PixelFormat);
            BitmapData dataResult = resultBmp.LockBits(rect, ImageLockMode.WriteOnly, originalBmp.PixelFormat);

            int bytesPerPixel = Image.GetPixelFormatSize(originalBmp.PixelFormat) / 8;
            int height = originalBmp.Height;
            int width = originalBmp.Width;


            // Set lightening coefficient – for example 0.7 gives more white than (orig + 255)/2
            float lightenFactor = 0.7f;

            unsafe
            {
                byte* ptrOrig = (byte*)dataOrig.Scan0;
                byte* ptrOverlay = (byte*)dataOverlay.Scan0;
                byte* ptrResult = (byte*)dataResult.Scan0;

                Parallel.For(0, height, y =>
                {
                    byte* rowOrig = ptrOrig + y * dataOrig.Stride;
                    byte* rowOverlay = ptrOverlay + y * dataOverlay.Stride;
                    byte* rowResult = ptrResult + y * dataResult.Stride;

                    for (int x = 0; x < width; x++)
                    {
                        int i = x * bytesPerPixel;
                        byte bOverlay = rowOverlay[i];
                        byte gOverlay = rowOverlay[i + 1];
                        byte rOverlay = rowOverlay[i + 2];

                        // If mask pixel is "nearly white", lighten original pixel
                        if (IsNearlyWhite(rOverlay, gOverlay, bOverlay))
                        {
                            // Calculate new pixel as mixture of original and white
                            rowResult[i] = (byte)Math.Min(255, (rowOrig[i] * (1 - lightenFactor) + 255 * lightenFactor));
                            rowResult[i + 1] = (byte)Math.Min(255, (rowOrig[i + 1] * (1 - lightenFactor) + 255 * lightenFactor));
                            rowResult[i + 2] = (byte)Math.Min(255, (rowOrig[i + 2] * (1 - lightenFactor) + 255 * lightenFactor));
                            if (bytesPerPixel == 4)
                                rowResult[i + 3] = rowOrig[i + 3]; // preserve alpha channel
                        }
                        else
                        {
                            // Otherwise copy the original pixel
                            for (int b = 0; b < bytesPerPixel; b++)
                                rowResult[i + b] = rowOrig[i + b];
                        }
                    }
                });
            }

            originalBmp.UnlockBits(dataOrig);
            overlayBmp.UnlockBits(dataOverlay);
            resultBmp.UnlockBits(dataResult);

            return resultBmp;
        }


        private DrawingImage RenderCurrentPageWithSelections()
        {
            // Get selections for the current page
            var blocksForThisPage = redactionBlocks.Where(b => b.PageNumber == currentPage);
            if (!blocksForThisPage.Any())
                return RenderOriginalPage(currentPage);

            // 1. Render the original page
            Bitmap originalBmp = new Bitmap(RenderOriginalPage(currentPage));

            // 2. Create overlay of same dimensions, in 32bpp format (to preserve alpha channel if needed)
            Bitmap overlayBmp = new Bitmap(originalBmp.Width, originalBmp.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(overlayBmp))
            {
                // Fill background with black – means no modification (black pixels don't meet IsNearlyWhite condition)
                g.Clear(System.Drawing.Color.Black);

                // For each selection draw white rectangle
                foreach (var block in blocksForThisPage)
                {
                    // Assume block.Bounds is given in original page units.
                    // To get pixel coordinates, multiply by scaleFactor.
                    RectangleF rect = new RectangleF(
                        block.Bounds.X * scaleFactor,
                        block.Bounds.Y * scaleFactor,
                        block.Bounds.Width * scaleFactor,
                        block.Bounds.Height * scaleFactor);

                    using (SolidBrush brush = new SolidBrush(System.Drawing.Color.White))
                    {
                        g.FillRectangle(brush, rect);
                    }
                }
            }

            // 3. Combine original bitmap and overlay – CombineBitmaps function
            Bitmap resultBmp = CombineBitmaps(originalBmp, overlayBmp);
            return resultBmp;
        }


        private DrawingImage RenderCurrentPageWithPdfCleanUp()
        {
            // Get selections for the current page
            var blocksForThisPage = redactionBlocks.Where(b => b.PageNumber == currentPage);
            if (blocksForThisPage.Any())
            {
                // If safeMode is selected – redaction on bitmap
                if (safeModeCheckBox.Checked || pdfCleanUpToolError)
                {
                    // 1. Render original page to bitmap
                    Bitmap originalBmp = new Bitmap(RenderOriginalPage(currentPage));

                    // 2. Based on selections (redactionBlocks) draw white rectangles on this bitmap.
                    // Assume scaleFactor coefficient is already set.
                    // If you need to account for rotation, you can add additional logic (e.g. get rotation from pdf).
                    using (Graphics g = Graphics.FromImage(originalBmp))
                    {
                        foreach (var block in blocksForThisPage)
                        {
                            // Draw white rectangle – redaction (you can change color or transparency if needed)
                            using (SolidBrush brush = new SolidBrush(System.Drawing.Color.FromArgb(128, 255, 255, 255)))
                            {
                                System.Drawing.RectangleF rect = new System.Drawing.RectangleF((block.Bounds.X * scaleFactor), (block.Bounds.Y * scaleFactor), (block.Bounds.Width * scaleFactor), (block.Bounds.Height * scaleFactor));
                                g.FillRectangle(brush, rect);
                            }
                        }
                    }
                    return originalBmp;
                }
                else
                {
                    // Standard mode: redaction at PDF level using PdfCleanUpTool.
                    byte[] pdfBytes = ExtractPageToBytes(currentPage);
                        
                    using (MemoryStream inputMemoryStream = new MemoryStream(pdfBytes))
                    using (MemoryStream outputMemoryStream = new MemoryStream())
                    {
                        using (iText.Kernel.Pdf.PdfDocument pdfDoc = new iText.Kernel.Pdf.PdfDocument(
                            new iText.Kernel.Pdf.PdfReader(inputMemoryStream),
                            new iText.Kernel.Pdf.PdfWriter(outputMemoryStream)))
                        {
                            PdfCleanUpTool cleanUpTool = new PdfCleanUpTool(pdfDoc);
                            var rotation = GetEffectiveRotationDegrees(currentPage);

                            foreach (var block in blocksForThisPage)
                            {
                                var pdfCoordinates = ConvertToPdfCoordinates(block.Bounds, currentPage, rotation);
                                iText.Kernel.Geom.Rectangle rectangle = new iText.Kernel.Geom.Rectangle(
                                    (int)Math.Round(pdfCoordinates.X),
                                    (int)Math.Round(pdfCoordinates.Y),
                                    (int)Math.Round(pdfCoordinates.Width),
                                    (int)Math.Round(pdfCoordinates.Height));
                                PdfCleanUpLocation cleanUpLocation = new PdfCleanUpLocation(1, rectangle, new iText.Kernel.Colors.DeviceRgb(255, 255, 255));
                                cleanUpTool.AddCleanupLocation(cleanUpLocation);
                            }
                            cleanUpTool.CleanUp();
                        }

                        Bitmap overlayBmp;
                        using (var pdfDocument = new PDFiumSharp.PdfDocument(data: outputMemoryStream.ToArray(), password: userPassword))
                        {
                            var page = pdfDocument.Pages[0];
                            using (var bmp = new PDFiumSharp.PDFiumBitmap(
                                (int)(page.Width * scaleFactor),
                                (int)(page.Height * scaleFactor),
                                true))
                            {
                                bmp.FillRectangle(0, 0,
                                    (int)(page.Width * scaleFactor),
                                    (int)(page.Height * scaleFactor), 0xFFFFFFFF);
                                page.Render(renderTarget: bmp, flags: PDFiumSharp.Enums.RenderingFlags.Annotations);
                                using (MemoryStream ms = new MemoryStream())
                                {
                                    bmp.Save(ms);
                                    ms.Position = 0;
                                    overlayBmp = new Bitmap(DrawingImage.FromStream(ms));
                                }
                            }
                        }

                        ApplyRotationOffset(overlayBmp, GetRotationOffset(currentPage));

                        // Render original page
                        Bitmap originalBmp = new Bitmap(RenderOriginalPage(currentPage));
                        // Combine bitmaps – CombineBitmaps function performs blending
                        Bitmap resultBmp = CombineBitmaps(originalBmp, overlayBmp);
                        return resultBmp;
                    }
                }
            }
            // If there are no selections, return original page.
            return RenderOriginalPage(currentPage);
        }

        private void CalculateMinScaleFactor(int pageNumber)
        {
            ZoomPanel panel = mainAppSplitContainer.Panel2.Controls[0] as ZoomPanel;
            if (!(panel is ZoomPanel)) return;

            // 1) Calculate dynamically min and max scale for THIS page
            int panelWidth = panel.ClientSize.Width;
            int panelHeight = panel.ClientSize.Height;

            var pageSize = GetPageSizeWithOffset(pageNumber);
            float pageW = pageSize.Width;
            float pageH = pageSize.Height;

            float scaleByWidth = panelWidth / pageW;
            float scaleByHeight = panelHeight / pageH;

            minScaleFactor = Math.Min(scaleByWidth, scaleByHeight);

            // To avoid falling into 0:
            if (minScaleFactor < 0.1f)
                minScaleFactor = 0.1f;
        }

        private void CalculateMaxScaleFactor()
        {
            maxScaleFactor = Math.Max(minScaleFactor * 3, 3.2f);
        }

        private void DisplayPdfPage(int pageNumber)
        {
            

            // 2) Correct scaleFactor if it went beyond [min, max]
            if ((scaleFactor < minScaleFactor))
            {
                scaleFactor = minScaleFactor;
            }
            else if ((scaleFactor > maxScaleFactor))
            {
                scaleFactor = maxScaleFactor;
            }

            pdfViewer.Image = RenderOriginalPage(pageNumber);
            renderTimer.Stop();
            renderTimer.Start();

            pageNumberTextBox.Text = pageNumber.ToString();
            numPagesLabel.Text = $"/ {numPages}";
            
            UpdateZoomButtons();
        }

        private void PageNumberTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            // We only allow you to press numbers and control keys.
            // char.IsControl(e.KeyChar) -> control keys (e.g. Backspace)
            // char.IsDigit(e.KeyChar)   -> numbers (0-9)
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
            {
                // If pressed key is not a digit nor a control key,
                // block it so it doesn't appear in text field.
                e.Handled = true;
            }
        }

        private void PageNumberTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                // For example convert entered text to int and execute action:
                if (int.TryParse(pageNumberTextBox.Text, out int pageNumber) && (pageNumber > 0) && (pageNumber <= numPages))
                {
                    // Here your logic, e.g. go to specific page:
                    
                    currentPage = pageNumber;
                    if ((string)filterComboBox.SelectedItem == allComboItem)
                    {
                        pagesListView.Items[currentPage - 1].Selected = true;
                        pagesListView.Items[currentPage - 1].EnsureVisible();
                    }
                    else
                    {
                        ListViewItem currentItem = FindListViewItemByPageNumber(currentPage);
                        if (currentItem != null)
                        {
                            currentItem.Selected = true;
                            currentItem.EnsureVisible();
                        }
                        else
                        {
                            pagesListView.SelectedItems.Clear();
                        }
                        ReloadRefreshCurrentPage();
                    }
                    UpdateNavigationButtons(currentPage);
                }
                else
                {
                    MessageBox.Show(this, Resources.Err_InvalidPageNumber, Resources.Title_Error, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                }

                // You may want to block "ding" sound in Windows:
                e.SuppressKeyPress = true;
                pageNumberTextBox.SelectAll();
            }
        }
        private void PageNumberTextBox_Click(object sender, EventArgs e)
        {
            // Select all text in the field
            pageNumberTextBox.SelectAll();
        }

        private static string NormalizeFontName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            // Remove suffixes like "(TrueType)" / "(OpenType)"
            int idx = name.IndexOf('(');
            if (idx >= 0)
                name = name.Substring(0, idx);

            name = name.Trim();

            // Normalize whitespace to single spaces
            name = string.Join(" ", name.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));

            return name;
        }

        private static string RemoveWidthTokens(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return name;

            // Practical fallback for width/condensed/expanded variants that may not appear in registry names
            string[] tokensToRemove =
            {
                "semi condensed",
                "semicondensed",                
                "semi expanded",
                "semiexpanded",
                "narrow",
                "wide",
                "condensed",
                "expanded"
            };

            string lowered = name.ToLowerInvariant();

            foreach (var token in tokensToRemove)
            {
                lowered = lowered.Replace(token, "");
            }

            // Normalize whitespace after token removal
            string cleaned = string.Join(" ",
                lowered.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));

            return cleaned;
        }

        private string GetFontFilePathFromRegistry(string fontFamilyName, FontStyle style)
        {
            if (string.IsNullOrWhiteSpace(fontFamilyName))
                return null;

            // Collect font registry entries from both HKLM and HKCU
            var entries = new List<(string DisplayName, string FileValue)>();

            void ReadFontsKey(Microsoft.Win32.RegistryKey root)
            {
                using (var regKey = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts"))
                {
                    if (regKey == null)
                        return;

                    foreach (string valueName in regKey.GetValueNames())
                    {
                        object value = regKey.GetValue(valueName);
                        if (value == null)
                            continue;

                        entries.Add((valueName, value.ToString()));
                    }
                }
            }

            ReadFontsKey(Microsoft.Win32.Registry.LocalMachine);
            ReadFontsKey(Microsoft.Win32.Registry.CurrentUser);

            if (entries.Count == 0)
                return null;

            // Normalize the target font family name (remove suffixes like "(TrueType)", normalize spaces, etc.)
            string targetNormalized = NormalizeFontName(fontFamilyName);

            // Some fonts are stored in registry under a "base" family name only (e.g. Bahnschrift),
            // while GDI/UI can show width variants like "Bahnschrift Condensed".
            // This base-normalized value is used as a fallback match.
            string targetBaseNormalized = NormalizeFontName(RemoveWidthTokens(fontFamilyName));

            // Find the best matching registry entry using a simple scoring approach
            var candidates = new List<(int Score, string ValueName, string FileValue)>();

            foreach (var entry in entries)
            {
                string regValueName = entry.DisplayName ?? string.Empty;

                // Normalize registry display name for comparisons
                string regNormalized = NormalizeFontName(regValueName);
                string regBaseNormalized = NormalizeFontName(RemoveWidthTokens(regValueName));

                // Prefer TrueType/OpenType entries when possible
                bool isTrueTypeOrOpenType =
                    regValueName.IndexOf("truetype", StringComparison.OrdinalIgnoreCase) >= 0
                    || regValueName.IndexOf("opentype", StringComparison.OrdinalIgnoreCase) >= 0;

                // Name matching score:
                // exact match > starts-with > contains > base-name fallback
                int nameScore = 0;

                if (string.Equals(regNormalized, targetNormalized, StringComparison.OrdinalIgnoreCase))
                    nameScore = 100;
                else if (regNormalized.StartsWith(targetNormalized, StringComparison.OrdinalIgnoreCase))
                    nameScore = 80;
                else if (regNormalized.IndexOf(targetNormalized, StringComparison.OrdinalIgnoreCase) >= 0)
                    nameScore = 60;
                else if (!string.IsNullOrEmpty(targetBaseNormalized))
                {
                    // Base-name fallback (important for e.g. Bahnschrift Condensed -> Bahnschrift)
                    if (string.Equals(regBaseNormalized, targetBaseNormalized, StringComparison.OrdinalIgnoreCase))
                        nameScore = 55;
                    else if (regBaseNormalized.StartsWith(targetBaseNormalized, StringComparison.OrdinalIgnoreCase))
                        nameScore = 45;
                    else if (regBaseNormalized.IndexOf(targetBaseNormalized, StringComparison.OrdinalIgnoreCase) >= 0)
                        nameScore = 35;
                }

                if (nameScore == 0)
                    continue;

                // Style score:
                // Try to prefer Bold/Italic entries if they exist in registry value names,
                // but do NOT reject if they don't exist (many fonts are represented by a single file entry).
                int styleScore = 0;
                string regLower = regValueName.ToLowerInvariant();

                if (style.HasFlag(FontStyle.Bold) && regLower.Contains("bold"))
                    styleScore += 20;

                if (style.HasFlag(FontStyle.Italic) && (regLower.Contains("italic") || regLower.Contains("oblique")))
                    styleScore += 20;

                // Small bonus for TrueType/OpenType entries (optional but useful)
                int typeScore = isTrueTypeOrOpenType ? 5 : 0;

                int totalScore = nameScore + styleScore + typeScore;
                candidates.Add((totalScore, entry.DisplayName, entry.FileValue));
            }

            if (candidates.Count == 0)
                return null;

            // Select the best-scored entry
            var best = candidates.OrderByDescending(c => c.Score).First();

            // Build the final path:
            // if value is just a file name (e.g. "arial.ttf"), combine it with the system Fonts folder.
            string fontFile = best.FileValue;
            if (string.IsNullOrWhiteSpace(fontFile))
                return null;

            if (!Path.IsPathRooted(fontFile))
            {
                string fontsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                fontFile = Path.Combine(fontsFolder, fontFile);
            }

            return File.Exists(fontFile) ? fontFile : null;
        }

        void RedactText(string inputFile, string outputFile, ISet<int> pagesWithBakedRotation = null)
        {
            iText.Kernel.Colors.Color cleanUpColorBlack = new DeviceRgb(0, 0, 0);
            iText.Kernel.Colors.Color cleanUpColorWhite = new DeviceRgb(255, 255, 255);

            if (File.Exists(outputFile))
            {
                try
                {
                    // Attempt to open file in exclusive mode
                    using (FileStream stream = File.Open(outputFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        stream.Close();
                    }
                }
                catch (IOException)
                {
                    MessageBox.Show(this, Resources.Err_OutputInUse,
                        Resources.Title_Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            byte[] pwdBytes = System.Text.Encoding.UTF8.GetBytes(userPassword);
            var readerProps = new ReaderProperties();
            if (!string.IsNullOrEmpty(userPassword))
            {
                readerProps.SetPassword(pwdBytes);
            }


            var writerProps = new WriterProperties();
            if (setSavePassword.Checked)
            {
                if (userNewPassword == null)
                {
                    userNewPassword = userPassword;
                }
                userNewPassword = PromptForPassword(userNewPassword, false);
                if (!string.IsNullOrEmpty(userNewPassword))
                {
                    byte[] pwdSaveBytes = System.Text.Encoding.UTF8.GetBytes(userNewPassword);
                    writerProps.SetStandardEncryption(
                        userPassword: pwdSaveBytes,
                        ownerPassword: pwdSaveBytes,
                        permissions: EncryptionConstants.ALLOW_PRINTING | EncryptionConstants.ALLOW_COPY,
                        encryptionAlgorithm: EncryptionConstants.ENCRYPTION_AES_256
                    );
                }
            }

            using (PdfReader reader = new PdfReader(inputFile, readerProps).SetUnethicalReading(Properties.Settings.Default.IgnorePdfRestrictions))
            using (PdfWriter writer = new PdfWriter(outputFile, writerProps))
            using (iText.Kernel.Pdf.PdfDocument pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader, writer))
            {
                ApplyRotationOffsetsToDocument(pdfDoc, pagesWithBakedRotation);
                LogDebug($"RedactText input={inputFile} output={outputFile} safeMode={safeModeCheckBox.Checked} cleanupError={pdfCleanUpToolError} blocks={redactionBlocks.Count} bakedPages={(pagesWithBakedRotation == null ? 0 : pagesWithBakedRotation.Count)}");

                if (signatures.Count > 0 && !signaturesOriginalRadioButton.Checked)
                {
                    if (signaturesReportRadioButton.Checked)
                    {
                        pdfDoc.AddNewPage(iText.Kernel.Geom.PageSize.A4);
                        int lastPageNumber = pdfDoc.GetNumberOfPages();

                        var page = pdfDoc.GetPage(lastPageNumber);
                        var pdfCanvas = new iText.Kernel.Pdf.Canvas.PdfCanvas(page);

                        iText.Kernel.Font.PdfFont font = iText.Kernel.Font.PdfFontFactory.CreateFont(
                            iText.IO.Font.Constants.StandardFonts.HELVETICA, "Cp1250");

                        int shiftStart = 750;
                        pdfCanvas.BeginText()
                                 .SetFontAndSize(font, 14)
                                 .MoveText(50, shiftStart)
                                 .ShowText(Resources.Signatures_Report_Title)
                                 .EndText();

                        shiftStart -= 10;
                        int shift = 25;

                        foreach (SignatureInfo sig in signatures)
                        {
                            if (sig.SignerName != "")
                            {
                                shiftStart -= shift;
                                pdfCanvas.BeginText()
                                     .SetFontAndSize(font, 12)
                                     .MoveText(50, shiftStart)
                                     .ShowText($"   {Resources.Signatures_Report_Field_SignerName}: {sig.SignerName}")
                                     .EndText();
                            }
                            if (sig.SignerTitle != "")
                            {
                                shiftStart -= shift;
                                pdfCanvas.BeginText()
                                 .SetFontAndSize(font, 12)
                                 .MoveText(50, shiftStart)
                                 .ShowText($"   {Resources.Signatures_Report_Field_SignerTitle}: {sig.SignerTitle}")
                                 .EndText();
                            }
                            if (sig.SignDate.ToString() != "")
                            {
                                shiftStart -= shift;
                                pdfCanvas.BeginText()
                                 .SetFontAndSize(font, 12)
                                 .MoveText(50, shiftStart)
                                 .ShowText($"   {Resources.Signatures_Report_Field_SignDate}: {sig.SignDate}")
                                 .EndText();
                            }
                            shiftStart -= 10;
                        }
                    }

                    if (signaturesRemoveRadioButton.Checked || signaturesReportRadioButton.Checked)
                    {
                        PdfAcroForm form = PdfAcroForm.GetAcroForm(pdfDoc, false);
                        if (form != null)
                        {
                            // Get all form fields
                            IDictionary<string, PdfFormField> fields = form.GetAllFormFields();

                            // Collect signature field keys for removal
                            // (cannot modify collection during iteration)
                            var signatureFieldKeys = fields
                                .Where(f => f.Value is PdfSignatureFormField)
                                .Select(f => f.Key)
                                .ToList();

                            List<string> signatureKeysToRemove = signatureFieldKeys;
                            if (signaturesRemoveRadioButton.Checked && hasCustomSignatureSelection)
                            {
                                var selected = new HashSet<string>(signaturesToRemove, StringComparer.OrdinalIgnoreCase);
                                signatureKeysToRemove = signatureFieldKeys
                                    .Where(key => selected.Contains(key))
                                    .ToList();
                            }

                            // Remove signature fields
                            foreach (var key in signatureKeysToRemove)
                            {
                                form.RemoveField(key);
                            }

                            if (signatureKeysToRemove.Count == signatureFieldKeys.Count)
                            {
                                pdfDoc.GetCatalog().Remove(PdfName.Perms);
                            }
                        }
                    }

                }

                if (signatures.Count > 0 && signaturesOriginalRadioButton.Checked)
                {
                    PdfAcroForm form = PdfAcroForm.GetAcroForm(pdfDoc, false);
                    if (form != null)
                    {
                        // Get all form fields
                        IDictionary<string, PdfFormField> fields = form.GetAllFormFields();
                        foreach (var field in fields)
                        {
                            if (field.Value is PdfSignatureFormField sigField)
                            {

                                foreach (var widget in sigField.GetWidgets())
                                {
                                    var page = widget.GetPage();
                                    if (page == null) continue;

                                    // 1) Get /AP /N (normal appearance) as Form XObject
                                    var apDict = widget.GetAppearanceDictionary();
                                    if (apDict == null) continue;
                                    var nStream = apDict.GetAsStream(PdfName.N);
                                    if (nStream == null) continue;

                                    var xObj = new PdfFormXObject(nStream);

                                    // 2) Signature position (widget rectangle)
                                    KernelGeom.Rectangle rect = widget.GetRectangle().ToRectangle();

                                    // 3) Insert XObject into page content (below or above content)
                                    //    - before: "background" (overwriting by other elements will disappear)
                                    //    - after: "above" content (usually what you want)

                                    var canvas = new iText.Kernel.Pdf.Canvas.PdfCanvas(page.NewContentStreamAfter(), page.GetResources(), pdfDoc);

                                    int rotation = GetEffectiveRotationDegrees(pdfDoc.GetPageNumber(page));
                                    float dpiX, dpiY;
                                    using (Graphics g = pdfViewer.CreateGraphics())
                                    {
                                        dpiX = g.DpiX;
                                        dpiY = g.DpiY;
                                    }


                                    // DPI correction for pdfCoordinates
                                    float scaleX = 72f / dpiX; // Scaling factor for X axis
                                    float scaleY = 72f / dpiY; // Scaling factor for Y axis
                                    RectangleF scaledAnnotationBounds = new RectangleF(
                                       rect.GetX(),
                                       rect.GetY(),
                                       rect.GetWidth() * scaleX,
                                       rect.GetHeight() * scaleY
                                    );
                                    var pdfCoordinates = ConvertToPdfCoordinates(scaledAnnotationBounds, 1, rotation);

                                    // Adjust X coordinate based on alignment
                                    float adjustedX = pdfCoordinates.X;


                                    // 4) Scale XObject to rect size (BBox XObject defines its own layout)
                                    KernelGeom.Rectangle bbox = xObj.GetBBox().ToRectangle();
                                    float sx = rect.GetWidth() / bbox.GetWidth();
                                    float sy = rect.GetHeight() / bbox.GetHeight();

                                    // Save state, set transformation and draw
                                    canvas.SaveState();


                                    // 1) Move to the lower-left corner of the target rect
                                    canvas.ConcatMatrix(1, 0, 0, 1, rect.GetLeft(), rect.GetBottom());
                                    


                                    canvas.AddXObject(xObj);
                                    canvas.RestoreState();
                                }


                            }
                        }

                        // Collect signature field keys for removal
                        // (cannot modify collection during iteration)
                        var signatureFieldKeys = fields
                            .Where(f => f.Value is iText.Forms.Fields.PdfSignatureFormField)
                            .Select(f => f.Key)
                            .ToList();

                        // Remove signature fields
                        foreach (var key in signatureFieldKeys)
                        {
                            form.RemoveField(key);
                        }
                        pdfDoc.GetCatalog().Remove(PdfName.Perms);

                    }
                    // Optionally remove signature dictionary from document catalog
                    pdfDoc.GetCatalog().Remove(PdfName.Perms);
                }


                PdfCleanUpTool cleanUpTool = new PdfCleanUpTool(pdfDoc);
                // Group redaction blocks by pages
                var blocksByPage = redactionBlocks.GroupBy(b => b.PageNumber);
                var visitedPages = new HashSet<int>();

                foreach (var pageGroup in blocksByPage)
                {
                    int pageNum = pageGroup.Key;
                    var pageWithSelections = pdfDoc.GetPage(pageNum);
                    bool baseRotationBaked = pagesWithBakedRotation != null && pagesWithBakedRotation.Contains(pageNum);
                    var rotation = baseRotationBaked ? GetRotationOffset(pageNum) : GetEffectiveRotationDegrees(pageNum);
                    if (DebugLogEnabled && pageNum >= 1 && pageNum <= pdf.Pages.Count)
                    {
                        var pdfiumPage = pdf.Pages[pageNum - 1];
                        var size = pageWithSelections.GetPageSize();
                        LogDebug($"Redact page={pageNum} baseRot={GetBaseRotationDegrees(pageNum)} offset={GetRotationOffset(pageNum)} effectiveRot={GetEffectiveRotationDegrees(pageNum)} rotUsed={rotation} baked={baseRotationBaked} pdfium={pdfiumPage.Width}x{pdfiumPage.Height} itext={size.GetWidth()}x{size.GetHeight()}");
                    }
                    foreach (var block in pageGroup)
                    {
                        var pdfCoordinates = ConvertToPdfCoordinates(block.Bounds, pageNum, rotation, includeBaseRotation: !baseRotationBaked);
                        if (DebugLogEnabled)
                        {
                            LogDebug($"Redact block page={pageNum} rect={block.Bounds} -> pdfRect={pdfCoordinates}");
                        }
                        iText.Kernel.Geom.Rectangle rectangle = new iText.Kernel.Geom.Rectangle(
                            (int)Math.Round(pdfCoordinates.X),
                            (int)Math.Round(pdfCoordinates.Y),
                            (int)Math.Round(pdfCoordinates.Width),
                            (int)Math.Round(pdfCoordinates.Height)
                        );
                        PdfCleanUpLocation cleanUpLocation;
                        if (colorCheckBox.Checked)
                        {
                            cleanUpLocation = new PdfCleanUpLocation(pageNum, rectangle, cleanUpColorBlack);
                        }
                        else
                        {
                            cleanUpLocation = new PdfCleanUpLocation(pageNum, rectangle, cleanUpColorWhite);
                        }
                        cleanUpTool.AddCleanupLocation(cleanUpLocation);
                    }
                }

                try
                {
                    // Call cleanup
                    cleanUpTool.CleanUp();
                }
                catch (Exception)
                {
                    MessageBox.Show(this, Resources.Err_CannotAnonymizePdf, Resources.Title_Error,
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // --- Section for rendering captions from textAnnotations list ---
                // Iterate over each annotation to be rendered on the given page.
                foreach (var annotation in textAnnotations)
                {
                    // Make sure the page number is correct
                    if (annotation.PageNumber < 1 || annotation.PageNumber > pdfDoc.GetNumberOfPages())
                        continue;

                    iText.Kernel.Pdf.PdfPage page = pdfDoc.GetPage(annotation.PageNumber);
                    // Add a new text layer
                    var pdfCanvas = new iText.Kernel.Pdf.Canvas.PdfCanvas(page);

                    string fontPath = GetFontFilePathFromRegistry(annotation.AnnotationFont.FontFamily.Name, annotation.AnnotationFont.Style);
                    PdfFont pdfFont = PdfFontFactory.CreateFont(fontPath, PdfEncodings.IDENTITY_H, PdfFontFactory.EmbeddingStrategy.PREFER_EMBEDDED);
                    PdfFont symbolFont = null;
                    string symbolFontPath = GetFontFilePathFromRegistry("Segoe UI Symbol", FontStyle.Regular);
                    if (!string.IsNullOrEmpty(symbolFontPath))
                    {
                        symbolFont = PdfFontFactory.CreateFont(symbolFontPath, PdfEncodings.IDENTITY_H, PdfFontFactory.EmbeddingStrategy.PREFER_EMBEDDED);
                    }

                    string textValue = annotation.AnnotationText ?? string.Empty;
                    float fontSize = (float)annotation.AnnotationFont.Size;
                        
                    bool baseRotationBaked = pagesWithBakedRotation != null && pagesWithBakedRotation.Contains(annotation.PageNumber);
                    int rotation = baseRotationBaked ? GetRotationOffset(annotation.PageNumber) : GetEffectiveRotationDegrees(annotation.PageNumber);
                    int annotationRotation = NormalizeRotation(annotation.AnnotationRotation);

                    // Coordinate conversion - assume ConvertToPdfCoordinates works similarly to redaction blocks
                    // Get DPI from pdfViewer
                    float dpiX, dpiY;
                    float lineHeightPt = 0f;
                    float gdiAscentPt = 0f;
                    List<float> gdiLineWidthsPt = new List<float>();
                    string normalizedText = textValue.Replace("\r\n", "\n").Replace("\r", "\n");
                    string[] lines = normalizedText.Split(new[] { '\n' }, StringSplitOptions.None);
                    using (Graphics g = pdfViewer.CreateGraphics())
                    {
                        dpiX = g.DpiX;
                        dpiY = g.DpiY;
                        lineHeightPt = annotation.AnnotationFont.GetHeight(g) * (72f / dpiY);
                        FontFamily family = annotation.AnnotationFont.FontFamily;
                        int ascentDesignUnits = family.GetCellAscent(annotation.AnnotationFont.Style);
                        int emHeight = family.GetEmHeight(annotation.AnnotationFont.Style);
                        if (emHeight > 0)
                        {
                            gdiAscentPt = annotation.AnnotationFont.Size * ascentDesignUnits / emHeight;
                        }
                        using (StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone())
                        {
                            format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
                            format.Trimming = StringTrimming.None;
                            foreach (string line in lines)
                            {
                                SizeF textSize = g.MeasureString(line, annotation.AnnotationFont, int.MaxValue, format);
                                gdiLineWidthsPt.Add(textSize.Width * (72f / dpiX));
                            }
                        }
                    }


                    // DPI correction for pdfCoordinates
                    float scaleX = 72f / dpiX; // Scaling factor for X axis
                    float scaleY = 72f / dpiY; // Scaling factor for Y axis
                    RectangleF scaledAnnotationBounds = new RectangleF(
                       annotation.AnnotationBounds.X,
                       annotation.AnnotationBounds.Y,
                       annotation.AnnotationBounds.Width * scaleX,
                       annotation.AnnotationBounds.Height * scaleY
                    );
                    float maxGdiWidthPt = 0f;
                    float maxPdfWidth = 0f;
                    var lineRuns = new List<List<(string Text, PdfFont Font)>>();
                    var pdfLineWidths = new List<float>();
                    List<(string Text, PdfFont Font)> BuildRuns(string line)
                    {
                        var runs = new List<(string Text, PdfFont Font)>();
                        if (string.IsNullOrEmpty(line))
                            return runs;

                        PdfFont currentFont = pdfFont;
                        var buffer = new System.Text.StringBuilder();

                        foreach (char ch in line)
                        {
                            int code = ch;
                            PdfFont nextFont = pdfFont;
                            if (!pdfFont.ContainsGlyph(code) && symbolFont != null && symbolFont.ContainsGlyph(code))
                            {
                                nextFont = symbolFont;
                            }

                            if (nextFont != currentFont && buffer.Length > 0)
                            {
                                runs.Add((buffer.ToString(), currentFont));
                                buffer.Clear();
                            }
                            currentFont = nextFont;
                            buffer.Append(ch);
                        }

                        if (buffer.Length > 0)
                        {
                            runs.Add((buffer.ToString(), currentFont));
                        }

                        return runs;
                    }
                    for (int i = 0; i < lines.Length; i++)
                    {
                        float lineGdiWidth = gdiLineWidthsPt[i];
                        var runs = BuildRuns(lines[i]);
                        lineRuns.Add(runs);
                        float linePdfWidth = 0f;
                        foreach (var run in runs)
                        {
                            linePdfWidth += run.Font.GetWidth(run.Text, fontSize);
                        }
                        pdfLineWidths.Add(linePdfWidth);
                        if (lineGdiWidth > maxGdiWidthPt)
                        {
                            maxGdiWidthPt = lineGdiWidth;
                            maxPdfWidth = linePdfWidth;
                        }
                    }

                    int lineCount = Math.Max(lines.Length, 1);
                    float layoutWidth = maxGdiWidthPt;
                    float layoutHeight = lineHeightPt * lineCount;
                    float horizontalScaling = 1f;
                    if (maxPdfWidth > 0f && maxGdiWidthPt > 0f)
                    {
                        horizontalScaling = maxGdiWidthPt / maxPdfWidth;
                    }

                    if (string.IsNullOrEmpty(textValue))
                        continue;

                    int combinedRotation = NormalizeRotation(rotation - annotationRotation);
                    float textRotation = (float)(combinedRotation * Math.PI / 180.0);

                    float cos = (float)Math.Cos(textRotation);
                    float sin = (float)Math.Sin(textRotation);
                    float annotationRotationRadians = (float)(annotationRotation * Math.PI / 180.0);
                    float rotCos = (float)Math.Cos(annotationRotationRadians);
                    float rotSin = (float)Math.Sin(annotationRotationRadians);
                    PointF rotationOffset = GetRotationOffsetForBounds(annotationRotation, layoutWidth, layoutHeight);

                    pdfCanvas.SaveState();
                    pdfCanvas.BeginText();
                    pdfCanvas.SetFontAndSize(pdfFont, fontSize);
                    pdfCanvas.SetFillColor(new DeviceRgb(annotation.AnnotationColor.R, annotation.AnnotationColor.G, annotation.AnnotationColor.B));
                    if (Math.Abs(horizontalScaling - 1f) > 0.0001f)
                    {
                        pdfCanvas.SetHorizontalScaling(horizontalScaling * 100f);
                    }

                    for (int i = 0; i < lines.Length; i++)
                    {
                        string lineText = lines[i];
                        float lineWidthPt = gdiLineWidthsPt[i];
                        var runs = lineRuns[i];

                        float localX = 0f;
                        switch (annotation.AnnotationAlignment)
                        {
                            case System.Windows.Forms.HorizontalAlignment.Center:
                                localX = (layoutWidth - lineWidthPt) / 2f;
                                break;
                            case System.Windows.Forms.HorizontalAlignment.Right:
                                localX = layoutWidth - lineWidthPt;
                                break;
                        }

                        float localY = i * lineHeightPt;
                        float localBaseX = localX;
                        float localBaseY = localY + gdiAscentPt;
                        float rotatedX = localBaseX * rotCos - localBaseY * rotSin + rotationOffset.X;
                        float rotatedY = localBaseX * rotSin + localBaseY * rotCos + rotationOffset.Y;
                        PointF baselineView = new PointF(
                            scaledAnnotationBounds.X + rotatedX,
                            scaledAnnotationBounds.Y + rotatedY);
                        PointF baselinePdf = ConvertPointToPdfCoordinates(baselineView, annotation.PageNumber, rotation, includeBaseRotation: !baseRotationBaked);

                        pdfCanvas.SetTextMatrix(cos, sin, -sin, cos, baselinePdf.X, baselinePdf.Y);
                        if (!string.IsNullOrEmpty(lineText))
                        {
                            if (runs.Count > 0)
                            {
                                foreach (var run in runs)
                                {
                                    pdfCanvas.SetFontAndSize(run.Font, fontSize);
                                    pdfCanvas.ShowText(run.Text);
                                }
                            }
                            else
                            {
                                pdfCanvas.SetFontAndSize(pdfFont, fontSize);
                                pdfCanvas.ShowText(lineText);
                            }
                        }
                    }
                    pdfCanvas.EndText();
                    pdfCanvas.RestoreState();

                }
                // --- End of caption rendering section ---

                if (pagesToRemove.Count > 0)
                {
                    var pagesToRemoveSorted = pagesToRemove.OrderByDescending(p => p);
                    foreach (var p in pagesToRemoveSorted)
                    {
                        if (p >= 1 && p <= pdfDoc.GetNumberOfPages())
                        {
                            pdfDoc.RemovePage(p);
                        }
                    }
                }

                var info = pdfDoc.GetDocumentInfo();

                // Custom fields
                info.SetMoreInfo("iTextCopyright", "This document was processed using iText Core / Community under the AGPLv3 license. Copyright (c) iText Group NV.");
                info.SetMoreInfo("iTextLicense", "For more information see https://www.gnu.org/licenses/agpl-3.0.html and https://itextpdf.com/.");

                ApplyDemoWatermarkIfNeeded(pdfDoc);
                MessageBox.Show(this, Resources.Msg_PreviewSavedPdf, Resources.Title_Success, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        internal static void ApplyDemoWatermarkIfNeeded(iText.Kernel.Pdf.PdfDocument pdfDoc)
        {
            if (pdfDoc == null || !LicenseManager.RequiresDemoWatermark)
            {
                return;
            }

            try
            {
                ApplyDemoWatermark(pdfDoc);
            }
            catch (Exception ex)
            {
                LogDebug("Failed to apply demo watermark: " + ex.Message);
            }
        }

        private static void ApplyDemoWatermark(iText.Kernel.Pdf.PdfDocument pdfDoc)
        {
            string watermarkText = GetDemoWatermarkText();
            if (string.IsNullOrWhiteSpace(watermarkText))
            {
                return;
            }

            var font = PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA_BOLD);
            var watermarkColor = new DeviceRgb(220, 0, 0);
            var opacityState = new PdfExtGState().SetFillOpacity(0.15f);
            float angle = (float)(Math.PI * 55.0 / 180.0);

            for (int pageNumber = 1; pageNumber <= pdfDoc.GetNumberOfPages(); pageNumber++)
            {
                var page = pdfDoc.GetPage(pageNumber);
                var pageSize = page.GetPageSize();
                float fontSize = GetWatermarkFontSize(pageSize);

                var pdfCanvas = new iText.Kernel.Pdf.Canvas.PdfCanvas(page.NewContentStreamAfter(), page.GetResources(), pdfDoc);
                pdfCanvas.SaveState();
                pdfCanvas.SetExtGState(opacityState);

                var canvas = new Canvas(pdfCanvas, pageSize);
                try
                {
                    var paragraph = new iText.Layout.Element.Paragraph(watermarkText)
                        .SetFont(font)
                        .SetFontSize(fontSize)
                        .SetFontColor(watermarkColor);

                    canvas.ShowTextAligned(
                        paragraph,
                        pageSize.GetWidth() / 2f,
                        pageSize.GetHeight() / 2f,
                        pageNumber,
                        iText.Layout.Properties.TextAlignment.CENTER,
                        iText.Layout.Properties.VerticalAlignment.MIDDLE,
                        angle);
                }
                finally
                {
                    canvas.Close();
                }

                pdfCanvas.RestoreState();
            }
        }

        private static float GetWatermarkFontSize(iText.Kernel.Geom.Rectangle pageSize)
        {
            float baseSize = Math.Min(pageSize.GetWidth(), pageSize.GetHeight()) * 0.06f;
            return Math.Max(30f, Math.Min(84f, baseSize));
        }

        private static string GetDemoWatermarkText()
        {
            var text = Resources.ResourceManager.GetString("Demo_Watermark_Text", CultureInfo.CurrentUICulture);
            return string.IsNullOrWhiteSpace(text) ? "WERSJA DEMONSTRACYJNA ANONPDF PRO" : text;
        }

        private void ClearRedactionBlocks()
        {
            redactionBlocks.Clear();
            pdfViewer.Invalidate();
            UpdateSelectionNavigationButtons();
            foreach (PageItemStatus status in allPageStatuses)
            {
                status.HasSelections = false;
            }
            if ((string)filterComboBox.SelectedItem == allComboItem)
            {
                foreach (ListViewItem item in pagesListView.Items)
                {
                    if (item.Tag is PageItemStatus status && status.HasSelections)
                    {
                        // Ustawienie flagi na false
                        status.HasSelections = false;
                        // Refresh specific item to make the change visible
                        pagesListView.Invalidate(item.Bounds);
                    }
                }
            } else
            {
                ApplyFilter((string)filterComboBox.SelectedItem);
            }


            saveProjectButton.Enabled = true;
            saveProjectMenuItem.Enabled = true;
            projectWasChangedAfterLastSave = true;
            renderTimer.Stop();
            renderTimer.Start();
        }

        private void ClearCurrentPageRedactionBlock()
        {
            redactionBlocks.RemoveAll(block => block.PageNumber == currentPage);
            projectWasChangedAfterLastSave = true;

            RedactionBlock blocksByPage = redactionBlocks.FirstOrDefault(block => block.PageNumber == currentPage);
            if (blocksByPage == null)
            {
                PageItemStatus status = allPageStatuses[currentPage - 1];
                //var item = pagesListView.Items[currentPage - 1];
                //bool hasSearchResults = ((PageItemStatus)currentItem.Tag).HasSearchResults;
                //bool markedForDeletion = ((PageItemStatus)currentItem.Tag).MarkedForDeletion;
                //bool hasTextAnnotations = ((PageItemStatus)currentItem.Tag).HasTextAnnotations;
                
                status.HasSelections = false;
                

                if ((string)filterComboBox.SelectedItem == allComboItem)
                {
                    ListViewItem currentItem = pagesListView.Items[currentPage - 1];
                    UpdateItemTag(currentItem, currentPage, status.HasSelections, status.HasSearchResults, status.MarkedForDeletion, status.HasTextAnnotations);
                    
                }
                else
                {
                    // rebuild list according to filter
                    ApplyFilter((string)filterComboBox.SelectedItem);
                }
                pagesListView.Invalidate();
            }

            pdfViewer.Invalidate();
            UpdateSelectionNavigationButtons();
            projectWasChangedAfterLastSave = true;
            saveProjectButton.Enabled = true;
            saveProjectMenuItem.Enabled = true;
            renderTimer.Stop();
            renderTimer.Start();
        }

        private void ClearTextAnnotations()
        {
            textAnnotations.Clear();
            pdfViewer.Invalidate();
        }

        private string PromptForPassword(string pwd = "", bool iChangeRemoveDialog = false)
        {
            using (Form prompt = new Form())
            {
                prompt.Width = 400;
                prompt.Height = 150;
                if (iChangeRemoveDialog || !string.IsNullOrEmpty(pwd))
                {
                    prompt.Text = Resources.PwdPrompt_Title_CanChangeRemove;
                } else
                {
                    prompt.Text = Resources.PwdPrompt_Title_Enter;
                }
                    
                prompt.FormBorderStyle = FormBorderStyle.FixedDialog;
                prompt.StartPosition = FormStartPosition.CenterParent;
                prompt.MaximizeBox = false;
                prompt.MinimizeBox = false;

                Label textLabel = new Label() { Left = 10, Top = 20, AutoSize = true, Text = Resources.PwdPrompt_Label_Password };
                TextBox inputBox = new TextBox() { Left = 70, Top = 18, Width = 300, UseSystemPasswordChar = true, Text = pwd };
                Button confirmation = new Button() { Text = Resources.Merge_OK, Left = 300, Width = 70, Top = 50, DialogResult = DialogResult.OK };

                prompt.Controls.Add(textLabel);
                prompt.Controls.Add(inputBox);
                prompt.Controls.Add(confirmation);
                prompt.AcceptButton = confirmation;

                return prompt.ShowDialog(this) == DialogResult.OK ? inputBox.Text : "";
            }
        }

        private void LoadPdf()
        {
            userPassword = "";
            PdfReader reader = null;
            iText.Kernel.Pdf.PdfDocument pdfDoc = null;

            while (true)
            {
                try
                {
                    // 1) Try to open without password
                    reader = new PdfReader(inputPdfPath);
                    pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader);      // first decryption attempt will happen here
                    int pages = pdfDoc.GetNumberOfPages();   // ensures password verification
                    break;
                }
                catch (BadPasswordException)
                {
                    // 2) Ask user for password
                    string pwd = PromptForPassword();
                    if (string.IsNullOrEmpty(pwd))
                    {
                        MessageBox.Show(this, Resources.Msg_NoPasswordOpenCancelled,
                                        Resources.Title_Info, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    // 3) Try again with password
                    try
                    {
                        var props = new ReaderProperties().SetPassword(System.Text.Encoding.UTF8.GetBytes(pwd));
                        reader = new PdfReader(inputPdfPath, props);
                        pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader);
                        int pages = pdfDoc.GetNumberOfPages();   // will verify password correctness
                        userPassword = pwd;
                        break;                                   // sukces
                    }
                    catch (BadPasswordException)
                    {
                        MessageBox.Show(this, Resources.Err_IncorrectPassword,
                                        Resources.Title_Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        // loop will ask for password again
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, string.Format(Resources.Msg_OpenPdfError, ex.Message),
                                    Resources.Title_Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            pdfDoc.Close();
            reader.Close();

            try
            {
                pdf = new PDFiumSharp.PdfDocument(inputPdfPath, userPassword);
            }
            catch (Exception)
            {
                MessageBox.Show(this, Resources.Err_InvalidPdfFile, Resources.Title_Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            LogDebug($"LoadPdf path={inputPdfPath} password={(string.IsNullOrEmpty(userPassword) ? "no" : "yes")}");

            pdfCleanUpToolError = false;
            groupBoxFilter.Visible = false;
            pagesListView.Visible = false;
            currentPage = 1;
            scaleFactor = 0;

            minScaleButton = true;
            maxScaleButton = false;

            pageRotationOffsets.Clear();

            CalculateMinScaleFactor(currentPage);
            CalculateMaxScaleFactor();

            pendingScaleFactor = minScaleFactor;

            filterComboBox.SelectedIndex = 0;

            ClearRedactionBlocks();
            ClearPagesToRemove();
            ClearTextAnnotations();
            PdfTextSearcher.ClearCache();
            signaturesToRemove.Clear();
            hasCustomSignatureSelection = false;

            inputProjectPath = "";
            lastSavedProjectName = "";
            pageNumberTextBox.Visible = true;
            numPagesLabel.Visible = true;
            numPages = pdf.Pages.Count;
            groupBoxPages.Enabled = true;

            LogPdfPageInfo();

            pagesListView.Clear();
            searchTextBox.Text = string.Empty;
            searchResultLabel.Text = string.Empty;

            ClearSearchResult();

            
            allPageStatuses.Clear();

            // Fill list with e.g. 10 pages
            for (int i = 1; i <= numPages; i++)
            {

                // (Optional) store page number in Tag for easy later retrieval

                PageItemStatus newStatus = new PageItemStatus()
                {
                    PageNumber = i,
                    MarkedForDeletion = false,
                    HasSearchResults = false,
                    HasSelections = false,
                    HasTextAnnotations = false,
                    HasRotation = false
                };

                allPageStatuses.Add(newStatus);
                ListViewItem item = new ListViewItem(string.Format(Resources.UI_PageLabelFormat, i)) { Tag = newStatus };

                pagesListView.Items.Add(item);
            }
            
            groupBoxFilter.Visible = true;
            groupBoxFilter.Refresh();
            pagesListView.Visible = true;
            pagesListView.Items[currentPage - 1].Selected = true;
            pagesListView.Items[currentPage - 1].EnsureVisible();

            groupBoxSelections.Enabled = true;
            groupBoxPagesToRemove.Enabled = true;
            if (PdfDocumentContainsText())
            {
                groupBoxSearch.Enabled = true;
            }
            else
            {
                groupBoxSearch.Enabled = false;
            }
            
            saveProjectAsButton.Enabled = true;
            buttonRedactText.Enabled = true;
            colorCheckBox.Enabled = true;
            setSavePassword.Enabled = true;
            openSavedPDFCheckBox.Enabled = true;
            safeModeCheckBox.Enabled = true;
            
            saveProjectAsMenuItem.Enabled = true;
            savePdfMenuItem.Enabled = true;
            addTextMenuItem.Enabled = true;
            deletePageMenuItem.Enabled = true;
            rotatePageMenuItem.Enabled = true;
            copyToClipboardMenuItem.Enabled = true;
            exportGraphicsMenuItem.Enabled = true;

            removePageButton.Enabled = true;
            removePageRangeButton.Enabled = true;
            
            UpdateSaveGroupState();
            UpdateOptionsGroupState();

            UpdateNavigationButtons(currentPage);
            UpdateWindowTitle();
            ExtractSignatures();
            AddRecentFile(inputPdfPath);
            CloseSplashIfVisible();
        }

        private void CloseSplashIfVisible()
        {
            if (splashClosed || splashForm == null || splashForm.IsDisposed)
            {
                return;
            }

            splashClosed = true;
            splashForm.Close();
        }

        private void LogPdfPageInfo()
        {
            if (!DebugLogEnabled || pdf == null || string.IsNullOrWhiteSpace(inputPdfPath))
            {
                return;
            }

            try
            {
                var props = new ReaderProperties();
                if (!string.IsNullOrEmpty(userPassword))
                {
                    props.SetPassword(System.Text.Encoding.UTF8.GetBytes(userPassword));
                }
                using (PdfReader reader = new PdfReader(inputPdfPath, props).SetUnethicalReading(Properties.Settings.Default.IgnorePdfRestrictions))
                using (iText.Kernel.Pdf.PdfDocument pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader))
                {
                    int count = Math.Min(pdf.Pages.Count, pdfDoc.GetNumberOfPages());
                    LogDebug($"LoadPdf pages={count}");
                    for (int i = 1; i <= count; i++)
                    {
                        var pdfiumPage = pdf.Pages[i - 1];
                        var itextPage = pdfDoc.GetPage(i);
                        var size = itextPage.GetPageSize();
                        LogDebug($"Page {i}: pdfium={pdfiumPage.Width}x{pdfiumPage.Height} orient={pdfiumPage.Orientation} itext={size.GetWidth()}x{size.GetHeight()} rotate={itextPage.GetRotation()} offset={GetRotationOffset(i)}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug($"LogPdfPageInfo error: {ex.Message}");
            }
        }

        private bool ConfirmOpenNewPdf()
        {
            if (redactionBlocks.Count > 0 && projectWasChangedAfterLastSave)
            {
                string msqOutText = Resources.Msg_Confirm_OpenNewWithUnsavedSelections;
                DialogResult result = MessageBox.Show(this,
                    msqOutText,
                    Resources.Title_Confirmation,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Exclamation,
                    MessageBoxDefaultButton.Button2
                );

                if (result == DialogResult.No)
                {
                    return false;
                }
            }

            return true;
        }

        private void OpenPdfFromPath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            if (!ConfirmOpenNewPdf())
            {
                return;
            }

            inputPdfPath = filePath;
            LoadPdf();
        }

        private void LoadPdfButton_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = Resources.Dialog_Filter_PDF,
                Title = Resources.Dialog_Title_OpenPdf
            };

            if (openFileDialog.ShowDialog(this) == DialogResult.OK)
            {
                OpenPdfFromPath(openFileDialog.FileName);
            }
        }

        // Update navigation buttons state
        private void UpdateNavigationButtons(int numPage)
        {
            // Enable First/Previous only when not on the first page
            buttonFirst.Enabled = numPage > 1;
            buttonPrevious.Enabled = numPage > 1;

            // If the currently displayed page is not the last, enable buttons to navigate to next/last page.
            buttonNextPage.Enabled = numPage < numPages;
            buttonLast.Enabled = numPage < numPages;

            bool isMarked = pagesToRemove.Contains(numPage);
            UpdateRemovePageButtonVisual(isMarked);
        }

        private void RotateCurrentPageClockwise()
        {
            if (pdf == null)
                return;

            var pageSize = GetPageSizeWithOffset(currentPage);
            if (pageSize.Width <= 0 || pageSize.Height <= 0)
                return;

            float dpiX;
            float dpiY;
            using (Graphics g = pdfViewer.CreateGraphics())
            {
                dpiX = g.DpiX;
                dpiY = g.DpiY;
            }
            float pxToPtX = 72f / dpiX;
            float pxToPtY = 72f / dpiY;

            foreach (var block in redactionBlocks.Where(b => b.PageNumber == currentPage))
            {
                block.Bounds = RotateRectClockwise(block.Bounds, pageSize.Width, pageSize.Height);
            }

            foreach (var annotation in textAnnotations.Where(a => a.PageNumber == currentPage))
            {
                RectangleF boundsPt = new RectangleF(
                    annotation.AnnotationBounds.X,
                    annotation.AnnotationBounds.Y,
                    annotation.AnnotationBounds.Width * pxToPtX,
                    annotation.AnnotationBounds.Height * pxToPtY
                );

                boundsPt = RotateRectClockwise(boundsPt, pageSize.Width, pageSize.Height);

                annotation.AnnotationBounds = new RectangleF(
                    boundsPt.X,
                    boundsPt.Y,
                    boundsPt.Width / pxToPtX,
                    boundsPt.Height / pxToPtY
                );
            }

            int offset = GetRotationOffset(currentPage);
            SetRotationOffset(currentPage, NormalizeRotation(offset + 90));

            currentSelection = RectangleF.Empty;

            projectWasChangedAfterLastSave = true;
            saveProjectButton.Enabled = true;
            saveProjectMenuItem.Enabled = true;

            PageItemStatus status = allPageStatuses[currentPage - 1];
            status.HasRotation = pageRotationOffsets.ContainsKey(currentPage);
            if ((string)filterComboBox.SelectedItem == allComboItem)
            {
                ListViewItem currentItem = pagesListView.Items[currentPage - 1];
                UpdateItemTag(currentItem, currentPage, status.HasSelections, status.HasSearchResults, status.MarkedForDeletion, status.HasTextAnnotations);
                pagesListView.Invalidate(currentItem.Bounds);
            }
            else
            {
                ApplyFilter((string)filterComboBox.SelectedItem);
            }

            ReloadRefreshCurrentPage();
            pendingScaleFactor = scaleFactor;
            pdfViewer.Invalidate();
        }


        private void PreviousPage()
        {
            currentPage -= 1;
            if (currentPage < 1)
            {
                currentPage = 1;
            }
            if ((string)filterComboBox.SelectedItem == allComboItem)
            {
                pagesListView.Items[currentPage - 1].Selected = true;
                pagesListView.Items[currentPage - 1].EnsureVisible();
            }
            else
            {
                ListViewItem currentItem = FindListViewItemByPageNumber(currentPage);
                if (currentItem != null)
                {
                    currentItem.Selected = true;
                    currentItem.EnsureVisible();
                }
                else
                {
                    pagesListView.SelectedItems.Clear();
                }
                ReloadRefreshCurrentPage();
            }
            // Update button states
            UpdateNavigationButtons(currentPage);
        }

        private void NextPage()
        {
            currentPage += 1;
            if (currentPage > numPages)
            {
                currentPage = numPages;
            }

            if ((string)filterComboBox.SelectedItem == allComboItem)
            {
                pagesListView.Items[currentPage - 1].Selected = true;
                pagesListView.Items[currentPage - 1].EnsureVisible();
            }
            else
            {
                ListViewItem currentItem = FindListViewItemByPageNumber(currentPage);
                if (currentItem != null)
                {
                    currentItem.Selected = true;
                    currentItem.EnsureVisible();
                }
                else
                {
                    pagesListView.SelectedItems.Clear();
                }
                ReloadRefreshCurrentPage();
            }

            // Update button states
            UpdateNavigationButtons(currentPage);
        }

        private void ButtonPrevious_Click(object sender, EventArgs e)
        {
            PreviousPage();
        }

        private void ButtonNextPage_Click(object sender, EventArgs e)
        {
            NextPage();
        }

        private void FirstPage()
        {
            currentPage = 1;
            if ((string)filterComboBox.SelectedItem == allComboItem)
            {
                pagesListView.Items[currentPage - 1].Selected = true;
                pagesListView.Items[currentPage - 1].EnsureVisible();
            }
            else
            {
                ListViewItem currentItem = FindListViewItemByPageNumber(currentPage);
                if (currentItem != null)
                {
                    currentItem.Selected = true;
                    currentItem.EnsureVisible();
                }
                else
                {
                    pagesListView.SelectedItems.Clear();
                }
                ReloadRefreshCurrentPage();
            }
            // Update button states
            UpdateNavigationButtons(currentPage);
        }

        private void ButtonFirst_Click(object sender, EventArgs e)
        {
            FirstPage();
        }

        private void LastPage()
        {
            currentPage = numPages;
            if ((string)filterComboBox.SelectedItem == allComboItem)
            {
                pagesListView.Items[currentPage - 1].Selected = true;
                pagesListView.Items[currentPage - 1].EnsureVisible();
            } else
            {
                ListViewItem currentItem = FindListViewItemByPageNumber(currentPage);
                if (currentItem != null)
                {
                    currentItem.Selected = true;
                    currentItem.EnsureVisible();
                } else
                {
                    pagesListView.SelectedItems.Clear();
                }
                    ReloadRefreshCurrentPage();
            }

            // Update button states
            UpdateNavigationButtons(currentPage);
        }

        private void ButtonLast_Click(object sender, EventArgs e)
        {
            LastPage();
        }

        private void ButtonRedactText_Click(object sender, EventArgs e)
        {

            if (pagesToRemove.Count == numPages)
            {
                MessageBox.Show(this, Resources.Err_AllPagesMarked, Resources.Title_Warning, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (SaveFileDialog saveFileDialog = new SaveFileDialog())
            {
                saveFileDialog.Filter = Resources.Dialog_Filter_PDF;
                saveFileDialog.Title = Resources.Dialog_Title_SavePdfAs;
                saveFileDialog.FileName = $"{System.IO.Path.GetFileNameWithoutExtension(inputPdfPath)}_anon.pdf";

                if (saveFileDialog.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        this.Cursor = Cursors.WaitCursor;

                        string tempFile = Path.Combine(Path.GetTempPath(), $"pdfreencoded_{DateTime.Now.Ticks}.pdf");
                        bool retReencoding = ReencodePdfKeepOriginalCompression(inputPdfPath, tempFile);

                        if (retReencoding)
                        {
                            RedactText(tempFile, saveFileDialog.FileName, reencodedPages);
                        }
                        else
                        {
                            RedactText(inputPdfPath, saveFileDialog.FileName);
                        }


                        // Delete existing file if it exists
                        if (File.Exists(tempFile))
                        {
                            File.Delete(tempFile);
                        }
                        this.Cursor = Cursors.Default;
                        if (openSavedPDFCheckBox.Checked)
                        {
                            try
                            {
                                ProcessStartInfo psi = new ProcessStartInfo
                                {
                                    FileName = saveFileDialog.FileName,
                                    UseShellExecute = true
                                };
                                Process.Start(psi);
                            }
                            catch (System.ComponentModel.Win32Exception wex)
                            {
                                MessageBox.Show(this, string.Format(Resources.Err_NoAssociatedPdfApp, wex.Message), Resources.Title_Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                        OpenDiagnosticLogIfEnabled();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, string.Format(Resources.Err_Save, ex.Message), Resources.Title_Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        this.Cursor = Cursors.Default;
                    }
                }
            }
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            // Reset icon click flag
            isClickOnIcon = false;
            clickedIconType = IconType.None;
            annotationForIcon = null;

            if (e.Button == MouseButtons.Right)
            {
                renderTimer.Stop();
                isDrawing = false;
                startPoint = e.Location;
            }
            else if (e.Button == MouseButtons.Left)
            {
                startPoint = e.Location;
                
                // First check if annotation buttons were clicked (for each annotation on current page)

                float dpiX, dpiY;
                using (Graphics g = pdfViewer.CreateGraphics())
                {
                    dpiX = g.DpiX;
                    dpiY = g.DpiY;
                }
                
                foreach (var annotation in textAnnotations.Where(b => b.PageNumber == currentPage))
                {
                    // Calculate scaled annotation rectangle (same as in OnPaint)
                    System.Drawing.Rectangle annotationRect = new System.Drawing.Rectangle(
                        (int)(annotation.AnnotationBounds.X * scaleFactor),
                        (int)(annotation.AnnotationBounds.Y * scaleFactor),
                        (int)(annotation.AnnotationBounds.Width * scaleFactor * 72f / dpiX),
                        (int)(annotation.AnnotationBounds.Height * scaleFactor * 72f / dpiY)
                    );
                    
                    // Calculate button rectangles - placed above annotation
                    System.Drawing.Rectangle deleteIconRect = new System.Drawing.Rectangle(
                        annotationRect.Right - annotationsIconSize,
                        annotationRect.Top - annotationsIconSize - annotationsIconPadding,
                        annotationsIconSize, annotationsIconSize
                    );
                    System.Drawing.Rectangle duplicateIconRect = new System.Drawing.Rectangle(
                        annotationRect.Right - (annotationsIconSize * 2) - annotationsIconPadding,
                        annotationRect.Top - annotationsIconSize - annotationsIconPadding,
                        annotationsIconSize, annotationsIconSize
                    );
                    System.Drawing.Rectangle lockIconRect = new System.Drawing.Rectangle(
                        annotationRect.Right - (annotationsIconSize * 3) - 2 * annotationsIconPadding,
                        annotationRect.Top - annotationsIconSize - annotationsIconPadding,
                        annotationsIconSize, annotationsIconSize
                    );
                    System.Drawing.Rectangle editIconRect = new System.Drawing.Rectangle(
                        annotationRect.Right - (annotationsIconSize * 4) - 3 * annotationsIconPadding,
                        annotationRect.Top - annotationsIconSize - annotationsIconPadding,
                        annotationsIconSize, annotationsIconSize
                    );
                    if (editIconRect.Contains(e.Location))
                    {
                        isClickOnIcon = true;
                        clickedIconType = IconType.Edit;
                        annotationForIcon = annotation;
                        break;
                    } else if (lockIconRect.Contains(e.Location))
                    {
                        isClickOnIcon = true;
                        clickedIconType = IconType.Lock;
                        annotationForIcon = annotation;
                        break;
                    }
                    else if (duplicateIconRect.Contains(e.Location))
                    {
                        isClickOnIcon = true;
                        clickedIconType = IconType.Duplicate;
                        annotationForIcon = annotation;
                        break;
                    }
                    else if (deleteIconRect.Contains(e.Location))
                    {
                        isClickOnIcon = true;
                        clickedIconType = IconType.Delete;
                        annotationForIcon = annotation;
                        break;
                    }
                }

                // If click did not hit an icon, check for dragging an annotation
                if (!isClickOnIcon)
                {
                    // Try to find an annotation using scaled coordinates

                    annotationToMove = textAnnotations.FirstOrDefault(block =>
                        block.PageNumber == currentPage &&
                        !block.AnnotationIsLocked &&
                        new Rectangle(
                            (int)(block.AnnotationBounds.X * scaleFactor),
                            (int)(block.AnnotationBounds.Y * scaleFactor),
                            (int)(block.AnnotationBounds.Width * scaleFactor * 72f / dpiX),
                            (int)(block.AnnotationBounds.Height * scaleFactor * 72f / dpiY)
                        ).Contains(e.Location)
);

                    if (annotationToMove != null)
                    {
                        isMoving = true;
                        this.Cursor = Cursors.Hand;
                    }
                    else
                    {
                        isDrawing = true;
                        currentSelection = new System.Drawing.RectangleF(startPoint, Size.Empty);
                    }
                }
                pdfViewer.Invalidate();
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (isDrawing)
            {
                float x = Math.Min(startPoint.X, e.X);
                float y;
                float width = Math.Abs(e.X - startPoint.X);
                float height;
                if (markerRadioButton.Checked)
                {
                    if ((Control.ModifierKeys & Keys.Control) == Keys.Control)
                    {
                        y = Math.Min(startPoint.Y, e.Y);
                        height = Math.Abs(e.Y - startPoint.Y);
                    }
                    else
                    {
                        y = (startPoint.Y - (markerWidth * scaleFactor / 2));
                        height = (markerWidth * scaleFactor);
                    }
                }
                else
                {
                    y = Math.Min(startPoint.Y, e.Y);
                    height = Math.Abs(e.Y - startPoint.Y);
                }
                currentSelection = new System.Drawing.RectangleF(x, y, width, height);
                pdfViewer.Invalidate();
            } else if (isMoving && !isClickOnIcon)
            {
                if (annotationToMove != null && !annotationToMove.AnnotationIsLocked)
                {
                    // Calculate mouse offset
                    float dx = e.X - startPoint.X;
                    float dy = e.Y - startPoint.Y;
                    startPoint = e.Location;

                    // Get current annotation Bounds (in "scaled" units, i.e. independent of zoom)
                    RectangleF bounds = annotationToMove.AnnotationBounds;

                    // Calculate new position in units before scaling (e.g. PDF points)
                    float newX = bounds.X + dx / scaleFactor;
                    float newY = bounds.Y + dy / scaleFactor;

                    // Pobierz DPI z pdfViewer
                    float dpiX, dpiY;
                    using (Graphics g = pdfViewer.CreateGraphics())
                    {
                        dpiX = g.DpiX;
                        dpiY = g.DpiY;
                    }

                    // Pobierz rozmiary obszaru pdfViewer (w pikselach)
                    float clientWidth = pdfViewer.ClientSize.Width;
                    float clientHeight = pdfViewer.ClientSize.Height;

                    // Limit so annotation doesn't go beyond left and right edge
                    if (newX < - (bounds.Width * 72f / dpiX / 2))
                    {
                        newX = -bounds.Width * 72f / dpiX / 2;
                    }
                    else if (newX > clientWidth / scaleFactor - bounds.Width * 72f / dpiX / 2)
                    {
                        newX = clientWidth / scaleFactor - bounds.Width * 72f / dpiX / 2;
                    }

                    if (newY < -bounds.Height * 72f / dpiX / 2)
                    {
                        newY = -bounds.Height * 72f / dpiX / 2;
                    }
                    else if (newY > clientHeight / scaleFactor - bounds.Height * 72f / dpiX / 2)
                    {
                        newY = clientHeight / scaleFactor - bounds.Height * 72f / dpiX / 2;
                    }

                    // Update annotation position
                    annotationToMove.AnnotationBounds = new RectangleF(newX, newY, bounds.Width, bounds.Height);
                    pdfViewer.Invalidate();
                }

            }
        }


        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            if (pdf == null)
                return;

            
            //if ((string)filterComboBox.SelectedItem == allComboItem)
            //{
            

            PageItemStatus status = allPageStatuses[currentPage - 1];


            if (e.Button == MouseButtons.Left)
            {
                // If user clicked in icon button area
                if (isClickOnIcon && annotationForIcon != null)
                {
                    // Call appropriate action depending on clicked button
                    if (clickedIconType == IconType.Edit)
                    {
                        // For example: call annotation edit method
                        AddEditAnnotation(annotationForIcon);
                    }
                    else if (clickedIconType == IconType.Lock)
                    {
                        string msg;
                        if (annotationForIcon.AnnotationIsLocked)
                        {
                            annotationForIcon.AnnotationIsLocked = false;
                            pdfViewer.Invalidate();
                            msg = "Odblokowano";
                        }
                        else
                        {
                            annotationForIcon.AnnotationIsLocked = true;
                            msg = "Zablokowano";
                        }
                        pdfViewer.Invalidate();
                        MessageBox.Show(this, string.Format(Resources.Msg_TextPositionInfo, msg), Resources.Title_Info, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else if (clickedIconType == IconType.Delete)
                    {
                        string msqOutText = Resources.Msg_Confirm_DeleteTextAnnotation;
                        DialogResult result = MessageBox.Show(this,
                            msqOutText,
                            Resources.Title_Confirmation,
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Exclamation,
                            MessageBoxDefaultButton.Button2
                        );

                        if (result == DialogResult.Yes)
                        {
                            // For example: remove annotation from list
                            textAnnotations.Remove(annotationForIcon);

                            projectWasChangedAfterLastSave = true;
                            saveProjectButton.Enabled = true;
                            saveProjectMenuItem.Enabled = true;

                            
                            
                            status.HasTextAnnotations = textAnnotations.Any(rb => rb.PageNumber == currentPage);
                            

                            if ((string)filterComboBox.SelectedItem == allComboItem)
                            {
                                // only refresh this row
                                ListViewItem currentItem = pagesListView.Items[currentPage - 1];
                                UpdateItemTag(currentItem, currentPage, status.HasSelections, status.HasSearchResults, status.MarkedForDeletion, status.HasTextAnnotations);
                                pagesListView.Invalidate(currentItem.Bounds);
                            }
                            else
                            {
                                // rebuild list according to filter
                                ApplyFilter((string)filterComboBox.SelectedItem);
                            }

                        }

                    }
                    else if (clickedIconType == IconType.Duplicate)
                    {
                        DuplicateAnnotation(annotationForIcon);
                    }
                    // Refresh view
                    pdfViewer.Invalidate();
                }
                else
                {
                    if (isMoving)
                    {
                        isMoving = false;
                        this.Cursor = Cursors.Default;
                        projectWasChangedAfterLastSave = true;
                        saveProjectButton.Enabled = true;
                        saveProjectMenuItem.Enabled = true;
                    }
                    else
                    {
                        System.Drawing.RectangleF rect = new System.Drawing.RectangleF((currentSelection.X / scaleFactor), (currentSelection.Y / scaleFactor), (currentSelection.Width / scaleFactor), (currentSelection.Height / scaleFactor));
                        if (markerRadioButton.Checked)
                        {
                            renderTimer.Stop();
                            if (isDrawing && currentSelection.Width > markerWidth * scaleFactor)
                            {
                                redactionBlocks.Add(new RedactionBlock(rect, currentPage));
                                
                                

                                status.HasSelections = true;

                                if ((string)filterComboBox.SelectedItem == allComboItem)
                                {
                                    ListViewItem currentItem = pagesListView.Items[currentPage - 1];
                                    UpdateItemTag(currentItem, currentPage, status.HasSelections, status.HasSearchResults, status.MarkedForDeletion, status.HasTextAnnotations);
                                    pagesListView.Invalidate(currentItem.Bounds);
                                }
                                else
                                {
                                    // rebuild list according to filter
                                    ApplyFilter((string)filterComboBox.SelectedItem);
                                }

                                projectWasChangedAfterLastSave = true;
                                saveProjectButton.Enabled = true;
                                saveProjectMenuItem.Enabled = true;
                                UpdateSelectionNavigationButtons();
                                renderTimer.Stop();
                                renderTimer.Start();
                            }
                        }
                        else if (boxRadioButton.Checked)
                        {
                            renderTimer.Stop();
                            if (isDrawing && currentSelection.Width > markerWidth * scaleFactor && currentSelection.Height > markerHeight * scaleFactor)
                            {
                                redactionBlocks.Add(new RedactionBlock(rect, currentPage));

                                
                                status.HasSelections = true;

                                if ((string)filterComboBox.SelectedItem == allComboItem)
                                {
                                    ListViewItem currentItem = pagesListView.Items[currentPage - 1];
                                    UpdateItemTag(currentItem, currentPage, status.HasSelections, status.HasSearchResults, status.MarkedForDeletion, status.HasTextAnnotations);
                                    pagesListView.Invalidate(currentItem.Bounds);
                                }
                                else
                                {
                                    // rebuild list according to filter
                                    ApplyFilter((string)filterComboBox.SelectedItem);
                                }

                                
                                projectWasChangedAfterLastSave = true;
                                saveProjectButton.Enabled = true;
                                saveProjectMenuItem.Enabled = true;
                                UpdateSelectionNavigationButtons();
                                renderTimer.Stop();
                                renderTimer.Start();
                            }
                        }
                    }
                }

            }
            else if (e.Button == MouseButtons.Right)
            {
                renderTimer.Stop();
                var blockToRemove = redactionBlocks.FirstOrDefault(block => block.PageNumber == currentPage && block.Bounds.Contains((startPoint.X / scaleFactor), (startPoint.Y / scaleFactor)));

                if (blockToRemove != null)
                {
                    redactionBlocks.Remove(blockToRemove);

                    RedactionBlock blocksByPage = redactionBlocks.FirstOrDefault(block => block.PageNumber == currentPage);
                    if (blocksByPage == null)
                    {
                        

                        status.HasSelections = false;

                        if ((string)filterComboBox.SelectedItem == allComboItem)
                        {
                            ListViewItem currentItem = pagesListView.Items[currentPage - 1];
                            UpdateItemTag(currentItem, currentPage, status.HasSelections, status.HasSearchResults, status.MarkedForDeletion, status.HasTextAnnotations);
                            pagesListView.Invalidate(currentItem.Bounds);
                        }
                        else
                        {
                            // rebuild list according to filter
                            ApplyFilter((string)filterComboBox.SelectedItem);
                        }

                        
                    }

                    projectWasChangedAfterLastSave = true;
                    saveProjectButton.Enabled = true;
                    saveProjectMenuItem.Enabled = true;
                    UpdateSelectionNavigationButtons();
                    renderTimer.Stop();
                    renderTimer.Start();
                }
            }
            else if (e.Button == MouseButtons.Middle)
            {
                pdfViewer.Image = RenderOriginalPage(currentPage);
            }

            isDrawing = false;
            currentSelection = System.Drawing.Rectangle.Empty;
            pdfViewer.Invalidate();


            // Reset icon click flag
            isClickOnIcon = false;
            clickedIconType = IconType.None;
            annotationForIcon = null;

        }

        private void OnPaint(object sender, PaintEventArgs e)
        {
            // Draw current selection
            if (isDrawing && currentSelection.Width > 0 && currentSelection.Height > 0)
            {
                using (SolidBrush brush = new SolidBrush(System.Drawing.Color.FromArgb(128, 255, 0, 0)))
                {
                    e.Graphics.FillRectangle(brush, currentSelection);
                }
            }

            // Drawing saved redaction blocks for current page
            foreach (var block in redactionBlocks.Where(b => b.PageNumber == currentPage))
            {
                using (SolidBrush brush = new SolidBrush(System.Drawing.Color.FromArgb(128, 255, 0, 0)))
                {
                    System.Drawing.RectangleF rect = new System.Drawing.RectangleF((block.Bounds.X * scaleFactor), (block.Bounds.Y * scaleFactor), (block.Bounds.Width * scaleFactor), (block.Bounds.Height * scaleFactor));
                    e.Graphics.FillRectangle(brush, rect);
                }
            }

            // Drawing saved search results
            foreach (var location in searchLocations)
            {
                if (location.PageNumber == currentPage)
                {
                    float x = location.Rect.GetX();
                    float y = location.Rect.GetY();
                    float w = location.Rect.GetWidth();
                    float h = location.Rect.GetHeight();
                    int rotation = GetEffectiveRotationDegrees(location.PageNumber);

                    RectangleF rectF = new RectangleF(x, y, w, h);
                    var pdfCoordinates = ConvertToPdfCoordinates(rectF, currentPage, rotation);

                    // Default highlight – e.g. yellow
                    System.Drawing.Color highlightColor = System.Drawing.Color.FromArgb(128, 255, 215, 0);

                    // Scale rectangle
                    float sc_x = pdfCoordinates.X * scaleFactor;
                    float sc_y = pdfCoordinates.Y * scaleFactor;
                    float sc_w = pdfCoordinates.Width * scaleFactor;
                    float sc_h = pdfCoordinates.Height * scaleFactor;

                    using (Pen pen = new Pen(highlightColor, 3))
                    {
                        e.Graphics.DrawRectangle(pen, sc_x - sc_h * 0.2f, sc_y - sc_h * 0.4f, sc_w + sc_h * 0.4f, sc_h + sc_h * 0.8f);
                    }

                    if (searchLocations.IndexOf(location) == currentLocationIndex)
                    {
                        // If this is currently selected location, change color to gray
                        highlightColor = System.Drawing.Color.FromArgb(255, 128, 128, 128);

                        using (Pen pen = new Pen(highlightColor, 3))
                        {
                            e.Graphics.DrawRectangle(pen, sc_x - sc_h * 0.4f, sc_y - sc_h * 0.6f, sc_w + sc_h * 0.8f, sc_h + sc_h * 1.2f);
                        }

                    }
                }
            }

            // Drawing text annotation blocks with text
            foreach (var annotation in textAnnotations.Where(b => b.PageNumber == currentPage))
            {
                // Calculate rectangle – taking scale into account
                System.Drawing.Rectangle rect = new System.Drawing.Rectangle(
                    (int)(annotation.AnnotationBounds.X * scaleFactor),
                    (int)(annotation.AnnotationBounds.Y * scaleFactor),
                    (int)(annotation.AnnotationBounds.Width * scaleFactor * 72f / e.Graphics.DpiX),
                    (int)(annotation.AnnotationBounds.Height * scaleFactor * 72f / e.Graphics.DpiY)
                );
                
                // Draw annotation border
                using (Pen pen = new Pen(System.Drawing.Color.Green, 1))
                {
                    e.Graphics.DrawRectangle(pen, rect);
                }

                // Transform HorizontalAlignment to StringAlignment
                StringAlignment align;
                switch (annotation.AnnotationAlignment)
                {
                    case System.Windows.Forms.HorizontalAlignment.Left:
                        align = StringAlignment.Near;
                        break;
                    case System.Windows.Forms.HorizontalAlignment.Center:
                        align = StringAlignment.Center;
                        break;
                    case System.Windows.Forms.HorizontalAlignment.Right:
                        align = StringAlignment.Far;
                        break;
                    default:
                        align = StringAlignment.Near;
                        break;
                }

                // Set text formatting – horizontal and vertical alignment (center)

                StringFormat stringFormat = StringFormat.GenericTypographic;
                stringFormat.Alignment = align;
                stringFormat.LineAlignment = StringAlignment.Near;
                stringFormat.FormatFlags = StringFormatFlags.NoWrap;
                stringFormat.Trimming = StringTrimming.None;


                // DPI correction coefficient (assuming PDF uses 72 DPI)
                float dpiCorrection = 72f / e.Graphics.DpiY; // DpiY to DPI w pionie dla bitmapy

                int annotationRotation = NormalizeRotation(annotation.AnnotationRotation);
                SizeF baseSize = GetAnnotationSize(annotation.AnnotationText, annotation.AnnotationFont, 0);
                float layoutWidth = baseSize.Width * scaleFactor * 72f / e.Graphics.DpiX;
                float layoutHeight = baseSize.Height * scaleFactor * 72f / e.Graphics.DpiY;

                // Render text with scaled font
                using (SolidBrush brush = new SolidBrush(annotation.AnnotationColor))
                {
                    float scaledFontSize = annotation.AnnotationFont.Size * scaleFactor * dpiCorrection;
                    using (Font scaledFont = new Font(annotation.AnnotationFont.FontFamily, scaledFontSize, annotation.AnnotationFont.Style))
                    {
                        float rotationRadians = (float)(annotationRotation * Math.PI / 180.0);
                        float rotCos = (float)Math.Cos(rotationRadians);
                        float rotSin = (float)Math.Sin(rotationRadians);
                        PointF rotationOffset = GetRotationOffsetForBounds(annotationRotation, layoutWidth, layoutHeight);
                        float offsetX = rotationOffset.X;
                        float offsetY = rotationOffset.Y;

                        RectangleF layoutRect = new RectangleF(0, 0, layoutWidth, layoutHeight);
                        var state = e.Graphics.Save();
                        e.Graphics.Transform = new System.Drawing.Drawing2D.Matrix(
                            rotCos, rotSin, -rotSin, rotCos,
                            rect.X + offsetX, rect.Y + offsetY);
                        e.Graphics.DrawString(
                            annotation.AnnotationText,  // Tekst adnotacji
                            scaledFont,                  // Przeskalowana czcionka
                            brush,                       // Kolor tekstu
                            layoutRect,                  // Obszar renderowania
                            stringFormat               // Alignment settings
                        );
                        e.Graphics.Restore(state);
                    }
                }

                // --- Drawing buttons with icons above annotation box ---

                // Position buttons to be above annotation
                // DeleteButton: rightmost; edit button at the left end.
                Rectangle deleteIconRect = new Rectangle(
                    rect.Right - annotationsIconSize,
                    rect.Top - annotationsIconSize - annotationsIconPadding,
                    annotationsIconSize, annotationsIconSize
                );
                Rectangle duplicateIconRect = new Rectangle(
                    rect.Right - (annotationsIconSize * 2) - annotationsIconPadding,
                    rect.Top - annotationsIconSize - annotationsIconPadding,
                    annotationsIconSize, annotationsIconSize
                );
                Rectangle lockIconRect = new Rectangle(
                    rect.Right - (annotationsIconSize * 3) - 2 * annotationsIconPadding,
                    rect.Top - annotationsIconSize - annotationsIconPadding,
                    annotationsIconSize, annotationsIconSize
                );
                Rectangle editIconRect = new Rectangle(
                    rect.Right - (annotationsIconSize * 4) - 3 * annotationsIconPadding,
                    rect.Top - annotationsIconSize - annotationsIconPadding,
                    annotationsIconSize, annotationsIconSize
                );

                // Button colors: gray background, dark gray border
                System.Drawing.Color buttonBackground = SystemColors.Control; // gray background
                System.Drawing.Color buttonBorder = System.Drawing.Color.DarkGray; // ciemniejsza ramka

                // Funkcja pomocnicza do rysowania "przycisku"
                void DrawButtonIcon(Rectangle buttonRect, string iconCode)
                {
                    // Draw button background
                    using (SolidBrush bgBrush = new SolidBrush(buttonBackground))
                    {
                        e.Graphics.FillRectangle(bgBrush, buttonRect);
                    }
                    // Draw border
                    using (Pen borderPen = new Pen(buttonBorder, 1))
                    {
                        e.Graphics.DrawRectangle(borderPen, buttonRect);
                    }
                    // Use "Segoe MDL2 Assets" font to draw icon
                    using (Font iconFont = new Font("Segoe MDL2 Assets", 12.0F, FontStyle.Regular, System.Drawing.GraphicsUnit.Point))
                    {
                        StringFormat iconFormat = new StringFormat
                        {
                            Alignment = StringAlignment.Center,
                            LineAlignment = StringAlignment.Center
                        };
                        using (SolidBrush iconBrush = new SolidBrush(SystemColors.ControlText))
                        {
                            Rectangle shiftedRect = new Rectangle(
                                buttonRect.X + 1,
                                buttonRect.Y + 2,
                                buttonRect.Width,
                                buttonRect.Height
                            );
                            e.Graphics.DrawString(iconCode, iconFont, iconBrush, shiftedRect, iconFormat);
                        }
                    }
                }

                // Draw buttons:
                DrawButtonIcon(editIconRect, "\uE70F");   // Ikonka edycji
                if (annotation.AnnotationIsLocked)
                {
                    DrawButtonIcon(lockIconRect, "\uE72E");   // Ikonka blokowaia
                    
                } else
                {
                    DrawButtonIcon(lockIconRect, "\uE785");   // Ikonka odblokowaia
                }
                DrawButtonIcon(duplicateIconRect, "\uE8C8");  // Ikonka powielania
                DrawButtonIcon(deleteIconRect, "\uE74D");  // Ikonka usuwania
            }



        }

        private void ClearSelectionButton_Click(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show(this,
                Resources.Msg_Confirm_ClearAllSelections,
                Resources.Title_Confirmation,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Exclamation, MessageBoxDefaultButton.Button2
            );

            if (result == DialogResult.No)
            {
                return;
            }

            ClearRedactionBlocks();
        }

        private void ClearPageButton_Click(object sender, EventArgs e)
        {
            ClearCurrentPageRedactionBlock();
        }

        private void ReloadRefreshCurrentPage()
        {
            pageNumberTextBox.Text = currentPage.ToString();
            pageNumberTextBox.Refresh();

            CalculateMinScaleFactor(currentPage);
            CalculateMaxScaleFactor();

            if ((scaleFactor < minScaleFactor) || minScaleButton)
            {
                scaleFactor = minScaleFactor;
            }
            else if ((scaleFactor > maxScaleFactor) || maxScaleButton)
            {
                scaleFactor = maxScaleFactor;
            }
            pagingTimer.Stop();
            pagingTimer.Start();
        }


        private void PagesListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            // If at least one element is selected
            if (pagesListView.SelectedItems.Count > 0)
            {
                // Bierzemy pierwszy zaznaczony element
                ListViewItem selectedItem = pagesListView.SelectedItems[0];


                // Tekst elementu, np. "Strona 1"
                //string text = selectedItem.Text;


                // If we use Tag to store number:
                currentPage = ((PageItemStatus)selectedItem.Tag).PageNumber;

                ReloadRefreshCurrentPage();

                

                PageItemStatus status = allPageStatuses[currentPage - 1];
                // Check whether current page has selections
                bool hasSelectionsForThisPage = redactionBlocks.Any(rb => rb.PageNumber == currentPage);
                bool hasSearchResultsForThisPage = searchLocations.Any(sr => sr.PageNumber == currentPage);
                bool markedForDeletionForThisPage = pagesToRemove.Contains(currentPage);
                bool hasTextAnnotationsForThisPage = textAnnotations.Any(rb => rb.PageNumber == currentPage);

                status.HasSelections = hasSelectionsForThisPage;
                status.HasSearchResults = hasSearchResultsForThisPage;
                status.MarkedForDeletion = markedForDeletionForThisPage;
                status.HasTextAnnotations = hasTextAnnotationsForThisPage;
                status.HasRotation = pageRotationOffsets.ContainsKey(currentPage);

                //UpdateItemTag(selectedItem, pageNumber, hasSelectionsForThisPage, hasSearchResultsForThisPage, markedForDeletionForThisPage, hasTextAnnotationsForThisPage);

                //if ((string)filterComboBox.SelectedItem == allComboItem)
                //{
                    // only refresh this row
                    //ListViewItem currentItem = pagesListView.Items[currentPage - 1];
                UpdateItemTag(selectedItem, currentPage, status.HasSelections, status.HasSearchResults, status.MarkedForDeletion, status.HasTextAnnotations);
                //}
                //else
                //{
                    // rebuild list according to filter
                //    ApplyFilter((string)filterComboBox.SelectedItem);
                //}

                if (hasSearchResultsForThisPage)
                {
                    currentLocationIndex = searchLocations.FindIndex(location => location.PageNumber == currentPage);
                    pdfViewer.Invalidate();
                }

                // Enable/disable button depending on whether there are selections
                clearPageButton.Enabled = hasSelectionsForThisPage;
                UpdateNavigationButtons(currentPage);
                UpdateSelectionNavigationButtons();
                UpdateSearchNavigationButtons();
                //pagesListView.Invalidate();
                pagesListView.Invalidate(selectedItem.Bounds);
            }
        }



        private void ZoomAtPanelCenter(bool zoomIn)
        {
            // Assume "panel" is mainAppSplitContainer.Panel2 
            // i ma AutoScroll = true.
            this.Cursor = Cursors.WaitCursor;
            ZoomPanel panel = mainAppSplitContainer.Panel2.Controls[0] as ZoomPanel;

            // (A) Calculate the center of the visible "panel" area in its layout (scroll + half client).
            // Note: panel.HorizontalScroll.Value + panel.ClientSize.Width/2 gives us
            // center *relative to top-left corner* in "ScrollValue" pixels.
            int centerScreenX = panel.HorizontalScroll.Value + (panel.ClientSize.Width / 2);
            int centerScreenY = panel.VerticalScroll.Value + (panel.ClientSize.Height / 2);

            // (B) Transform this to "document" coordinates (before changing scaleFactor).
            // Because docCoordX = screenX / oldScale (assuming pdfViewer.Location = (0,0)).
            float docCoordX = centerScreenX / scaleFactor;
            float docCoordY = centerScreenY / scaleFactor;

            // (C) Change scaleFactor (e.g. +/- 0.2)
            if (zoomIn)
                scaleFactor += percentScaleFactor;
            else
                scaleFactor -= percentScaleFactor;

            CalculateMinScaleFactor(currentPage);
            CalculateMaxScaleFactor();

            if (scaleFactor < minScaleFactor)
            {
                minScaleButton = true;
                maxScaleButton = false;
                scaleFactor = minScaleFactor;
            }
            else
            {
                minScaleButton = false;
            }

            if (scaleFactor > maxScaleFactor)
            {
                minScaleButton = false;
                maxScaleButton = true;
                scaleFactor = maxScaleFactor;
            }
            else
            {
                maxScaleButton = false;
            }


            // (D) Call page re-display
            //     (ta metoda ustawi pdfViewer.Image = ... i pdfViewer.Size = ...)
            DisplayPdfPage(currentPage);

            // (E) Now we want to set scroll so that docCoordX/docCoordY
            //     is again in the center of the screen.
            int newScrollX = (int)(docCoordX * scaleFactor) - (panel.ClientSize.Width / 2);
            int newScrollY = (int)(docCoordY * scaleFactor) - (panel.ClientSize.Height / 2);

            // Protection against exceeding allowable scroll range
            if (newScrollX < panel.HorizontalScroll.Minimum) newScrollX = panel.HorizontalScroll.Minimum;
            if (newScrollX > panel.HorizontalScroll.Maximum) newScrollX = panel.HorizontalScroll.Maximum;
            if (newScrollY < panel.VerticalScroll.Minimum) newScrollY = panel.VerticalScroll.Minimum;
            if (newScrollY > panel.VerticalScroll.Maximum) newScrollY = panel.VerticalScroll.Maximum;

            // Set scroll:
            panel.AutoScrollPosition = new Point(newScrollX, newScrollY);
            // Or: panel.HorizontalScroll.Value = newScrollX; panel.VerticalScroll.Value = newScrollY;
            this.Cursor = Cursors.Default;
        }



        private void ZoomOutButton_Click(object sender, EventArgs e)
        {
            ZoomAtPanelCenter(false);
        }

        private void ZoomInButton_Click(object sender, EventArgs e)
        {
            ZoomAtPanelCenter(true);
        }


        private void ClearPagesToRemove()
        {
            foreach (int pageNum in pagesToRemove)
            {
                pagesListView.Items[pageNum - 1].BackColor = CurrentTheme.SectionBackColor;
                if (pagesListView.Items[pageNum - 1].ForeColor != System.Drawing.Color.Red)
                {
                    pagesListView.Items[pageNum - 1].ForeColor = CurrentTheme.TextPrimaryColor;
                }
            }
            pagesToRemove.Clear();
            UpdateRemovePageButtonVisual(false);
        }

        private void LoadRedactionBlocks(string inputProjectPathTemp)
        {
            if (redactionBlocks.Count > 0 && projectWasChangedAfterLastSave)
            {
                string msqOutText = Resources.Msg_Confirm_DiscardUnsavedRedactions;
                DialogResult result = MessageBox.Show(this,
                    msqOutText,
                    Resources.Title_Confirmation,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Exclamation,
                    MessageBoxDefaultButton.Button2
                );

                if (result == DialogResult.No)
                {
                    return;
                }
            }


            if (File.Exists(inputProjectPathTemp))
            {
                string json = File.ReadAllText(inputProjectPathTemp);

                try
                {
                    ProjectData projectData = JsonConvert.DeserializeObject<ProjectData>(json);
                    var filePath = projectData.FilePath ?? "";

                    if (filePath !="" && filePath != inputPdfPath)
                    {
                        inputPdfPath = filePath;
                        LoadPdf();
                    }

                    if (filePath == "")
                    {
                        MessageBox.Show(this,
                            Resources.Msg_OpenPdfFirst,
                            Resources.Title_Warning,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Exclamation
                        );
                        return;
                    }
                    List<RedactionBlock> redactionBlocksJson = projectData.RedactionBlocks ?? new List<RedactionBlock>();
                    HashSet<int> pagesToRemoveJson = projectData.PagesToRemove ?? new HashSet<int>();
                    List<TextAnnotation> textAnnotationsJson = projectData.TextAnnotations ?? new List<TextAnnotation>();
                    Dictionary<int, int> rotationOffsetsJson = projectData.PageRotationOffsets ?? new Dictionary<int, int>();
                    List<string> signaturesToRemoveJson = projectData.SignaturesToRemove;
                    string signaturesMode = projectData.SignaturesMode;

                    if (redactionBlocksJson.Count > 0)
                    {
                        int maxPageNumber = redactionBlocksJson.Max(rb => rb.PageNumber);
                        if (maxPageNumber > numPages)
                        {
                            MessageBox.Show(this, Resources.Err_SelectValidProjectFile, Resources.Title_Warning, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                            return;
                        }
                    }
 
                    ClearRedactionBlocks();
                    redactionBlocks = redactionBlocksJson;

                    ClearPagesToRemove();
                    pagesToRemove = pagesToRemoveJson;

                    ClearTextAnnotations();
                    textAnnotations = textAnnotationsJson; 

                    pageRotationOffsets = rotationOffsetsJson
                        .Where(kvp => kvp.Key >= 1 && kvp.Key <= numPages)
                        .Select(kvp => new KeyValuePair<int, int>(kvp.Key, NormalizeRotation(kvp.Value)))
                        .Where(kvp => kvp.Value != 0)
                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                    hasCustomSignatureSelection = signaturesToRemoveJson != null;
                    signaturesToRemove = signaturesToRemoveJson ?? new List<string>();
                    SyncSignatureSelectionWithAvailableSignatures();
                    UpdateSignatureSelectionMenuState();

                    ApplySignatureModeFromProject(signaturesMode);

                    if (projectData.CurrentPage > 0)
                    {
                        currentPage = Math.Max(1, Math.Min(projectData.CurrentPage, numPages));
                    }

                    foreach (var statusItem in allPageStatuses)
                    {
                        statusItem.HasRotation = false;
                    }
                    foreach (var kvp in pageRotationOffsets)
                    {
                        int pageNum = kvp.Key;
                        allPageStatuses[pageNum - 1].HasRotation = true;
                    }

                    ReloadRefreshCurrentPage();

                    pdfViewer.Invalidate();
                    UpdateSelectionNavigationButtons();

                    PageItemStatus status;

                    foreach (var block in redactionBlocks)
                    {
                        status = allPageStatuses[block.PageNumber - 1];
                        var item = pagesListView.Items[block.PageNumber - 1];
                        //bool hasSearchResults = ((PageItemStatus)item.Tag).HasSearchResults;
                        //bool markedForDeletion = ((PageItemStatus)item.Tag).MarkedForDeletion;
                        //bool hasTextAnnotations = ((PageItemStatus)item.Tag).HasTextAnnotations;
                        status.HasSelections = true;
                        //allPageStatuses.Add(status);
                        //UpdateItemTag(item, block.PageNumber, true, hasSearchResults, markedForDeletion, hasTextAnnotations);
                        UpdateItemTag(item, block.PageNumber, status.HasSelections, status.HasSearchResults, status.MarkedForDeletion, status.HasTextAnnotations);
                        pagesListView.Invalidate(item.Bounds);
                    }

                    foreach (var block in textAnnotations)
                    {
                        status = allPageStatuses[block.PageNumber - 1];
                        var item = pagesListView.Items[block.PageNumber - 1];
                        //bool hasSelections = ((PageItemStatus)item.Tag).HasSelections;
                        //bool hasSearchResults = ((PageItemStatus)item.Tag).HasSearchResults;
                        //bool markedForDeletion = ((PageItemStatus)item.Tag).MarkedForDeletion;
                        //UpdateItemTag(item, block.PageNumber, hasSelections, hasSearchResults, markedForDeletion, true);
                        status.HasTextAnnotations = true;
                        UpdateItemTag(item, block.PageNumber, status.HasSelections, status.HasSearchResults, status.MarkedForDeletion, status.HasTextAnnotations);
                        pagesListView.Invalidate(item.Bounds);
                    }

                    foreach (var pageNum in pagesToRemove)
                    {
                        status = allPageStatuses[pageNum - 1];
                        if (pageNum == 1)
                        {
                            UpdateRemovePageButtonVisual(true);
                        }
                        var item = pagesListView.Items[pageNum - 1];

                        //bool hasSelections = ((PageItemStatus)item.Tag).HasSelections;
                        //bool hasSearchResults = ((PageItemStatus)item.Tag).HasSearchResults;
                        //bool hasTextAnnotations = ((PageItemStatus)item.Tag).HasTextAnnotations;
                        //UpdateItemTag(item, pageNum, hasSelections, hasSearchResults, true, hasTextAnnotations);
                        //pagesListView.Invalidate();
                        status.MarkedForDeletion = true;
                        UpdateItemTag(item, pageNum, status.HasSelections, status.HasSearchResults, status.MarkedForDeletion, status.HasTextAnnotations);
                        pagesListView.Invalidate(item.Bounds);
                    }

                    foreach (ListViewItem item in pagesListView.Items)
                    {
                        int pageNum = ((PageItemStatus)item.Tag).PageNumber;
                        PageItemStatus currentStatus = allPageStatuses[pageNum - 1];
                        UpdateItemTag(item, pageNum, currentStatus.HasSelections, currentStatus.HasSearchResults, currentStatus.MarkedForDeletion, currentStatus.HasTextAnnotations);
                        pagesListView.Invalidate(item.Bounds);
                    }

                    if (numPages > 0)
                    {
                        if ((string)filterComboBox.SelectedItem == allComboItem)
                        {
                            pagesListView.Items[currentPage - 1].Selected = true;
                            pagesListView.Items[currentPage - 1].EnsureVisible();
                        }
                        else
                        {
                            ListViewItem currentItem = FindListViewItemByPageNumber(currentPage);
                            if (currentItem != null)
                            {
                                currentItem.Selected = true;
                                currentItem.EnsureVisible();
                            }
                            else
                            {
                                pagesListView.SelectedItems.Clear();
                            }
                        }
                    }

                    inputProjectPath = inputProjectPathTemp;
                    //Properties.Settings.Default.LastPapPath = inputProjectPath;
                    //Properties.Settings.Default.Save();

                    UpdateSelectionNavigationButtons();
                    UpdateWindowTitle();
                    projectWasChangedAfterLastSave = false;
                    AddRecentFile(inputProjectPathTemp);
                }
                catch (Exception)
                {
                    UpdateWindowTitle();
                    MessageBox.Show(this, Resources.Err_InvalidProjectFormat, Resources.Title_Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
        }

        private void SaveProjectAs()
        {
            using (SaveFileDialog saveFileDialog = new SaveFileDialog())
            {
                string filePapName = "";
                if (lastSavedProjectName != "")
                {
                    filePapName = lastSavedProjectName;
                }
                else if (inputProjectPath != "")
                {
                    filePapName = inputProjectPath;
                }
                else
                {
                    filePapName = inputPdfPath;
                }

                saveFileDialog.Filter = Resources.Dialog_Filter_PAP;
                saveFileDialog.Title = string.Format(Resources.Dialog_Title_SavePap, Branding.ProductName);
                saveFileDialog.FileName = $"{System.IO.Path.GetFileNameWithoutExtension(filePapName)}.pap";

                if (saveFileDialog.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        var jsonSettings = new JsonSerializerSettings
                        {
                            Formatting = Formatting.Indented // prettier, indented format
                        };


                        ProjectData projectData = new ProjectData
                        {
                            RedactionBlocks = redactionBlocks,
                            PagesToRemove = pagesToRemove,
                            TextAnnotations = textAnnotations,
                            PageRotationOffsets = new Dictionary<int, int>(pageRotationOffsets),
                            FilePath = inputPdfPath,
                            CurrentPage = currentPage,
                            SignaturesMode = GetSignatureModeForProject(),
                            SignaturesToRemove = hasCustomSignatureSelection ? new List<string>(signaturesToRemove) : null
                        };

                        // Serialize list to JSON string
                        string json = JsonConvert.SerializeObject(projectData, jsonSettings);

                        // Write to .pap file (plain text)
                        lastSavedProjectName = saveFileDialog.FileName;
                        File.WriteAllText(lastSavedProjectName, json);

                        DialogResult result = MessageBox.Show(this,
                            Resources.Msg_ProjectSaved,
                            Resources.Title_Success,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information
                        );
                        projectWasChangedAfterLastSave = false;
            saveProjectButton.Enabled = false;
            saveProjectMenuItem.Enabled = false;
                        Properties.Settings.Default.LastPapPath = lastSavedProjectName;
                        Properties.Settings.Default.Save();
                        UpdateWindowTitle();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, string.Format(Resources.Err_Save, ex.Message), Resources.Title_Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }

        }

        private void SaveProjectAsButton_Click(object sender, EventArgs e)
        {
            //if (redactionBlocks.Count == 0 && pagesToRemove.Count == 0)
            //{
            //    MessageBox.Show(this, "Select content or page to remove.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
            //    return;
            //}

            SaveProjectAs();
        }

        private void OpenProjectButton_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = Resources.Dialog_Filter_PAP,
                Title = string.Format(Resources.Dialog_Title_OpenPap, Branding.ProductName)
            };

            if (openFileDialog.ShowDialog(this) == DialogResult.OK)
            {
                // inputProjectPath = openFileDialog.FileName;

                //Properties.Settings.Default.LastPapPath = inputProjectPath;
                //Properties.Settings.Default.Save();
                //UpdateWindowTitle();

                LoadRedactionBlocks(openFileDialog.FileName);
            }
        }

        private List<int> GetPagesWithBlocks()
        {
            var pagesWithBlocks = redactionBlocks
                .Select(rb => rb.PageNumber)
                .Distinct()
                .OrderBy(page => page)
                .ToList();
            return pagesWithBlocks;
        }


        private void UpdateSelectionNavigationButtons()
        {
            var pagesWithBlocks = GetPagesWithBlocks();
            if (!pagesWithBlocks.Any())
            {
                selectionFirstButton.Enabled = false;
                selectionPrevButton.Enabled = false;
                selectionNextButton.Enabled = false;
                selectionLastButton.Enabled = false;
                clearSelectionButton.Enabled = false;
                clearPageButton.Enabled = false;
                return;
            }

            int firstSelectionPage = pagesWithBlocks.First();
            int lastSelectionPage = pagesWithBlocks.Last();

            // If the current page (currentPage) is less than or equal to the first page with blocks,
            // then you cannot go to the previous selection.
            selectionFirstButton.Enabled = currentPage > firstSelectionPage;
            selectionPrevButton.Enabled = currentPage > firstSelectionPage;

            // If the current page is greater than or equal to the last page with blocks,
            // then you cannot go to the next selection.
            selectionNextButton.Enabled = currentPage < lastSelectionPage;
            selectionLastButton.Enabled = currentPage < lastSelectionPage;

                // Check whether current page has selections
            bool hasSelectionsForThisPage = redactionBlocks.Any(rb => rb.PageNumber == currentPage);

            // Enable/disable button depending on whether there are selections
            clearPageButton.Enabled = hasSelectionsForThisPage;
            clearSelectionButton.Enabled = true;
        }

        private void UpdateSearchNavigationButtons()
        {
            // If no results found, disable all buttons.
            if (searchLocations == null || searchLocations.Count == 0)
            {
                searchFirstButton.Enabled = false;
                searchPrevButton.Enabled = false;
                searchNextButton.Enabled = false;
                searchLastButton.Enabled = false;
                return;
            }

            // Check if there is a search result on the current page preceding the current result.
            bool hasPrevSamePage = currentLocationIndex > 0 && searchLocations[currentLocationIndex - 1].PageNumber == currentPage;
            // Check if there is a result on earlier pages.
            bool hasPrevOtherPage = searchLocations.Any(loc => loc.PageNumber < currentPage);

            // Check if there is a search result on the current page following the current result.
            bool hasNextSamePage = currentLocationIndex < searchLocations.Count - 1 && searchLocations[currentLocationIndex + 1].PageNumber == currentPage;
            // Check if there is a result on following pages.
            bool hasNextOtherPage = searchLocations.Any(loc => loc.PageNumber > currentPage);

            // "First" is enabled when the current result is not the first globally.
            searchFirstButton.Enabled = currentLocationIndex > 0;
            // "Prev" active when we have a result earlier on the same page or on a previous page.
            searchPrevButton.Enabled = hasPrevSamePage || hasPrevOtherPage;
            // "Next" active when we have another result on the same page or on the next page.
            searchNextButton.Enabled = hasNextSamePage || hasNextOtherPage;
            // "Last" is enabled when the current result is not the last globally.
            searchLastButton.Enabled = currentLocationIndex < searchLocations.Count - 1;
        }


        private void SelectionFirstButton_Click(object sender, EventArgs e)
        {
            var pagesWithBlocks = GetPagesWithBlocks();
            if (!pagesWithBlocks.Any())
            {
                return;
            }
            int firstPage = pagesWithBlocks.First();
            if ((string)filterComboBox.SelectedItem == allComboItem)
            {
                pagesListView.Items[firstPage - 1].Selected = true;
                pagesListView.Items[firstPage - 1].EnsureVisible();
            } else
            {
                ListViewItem currentItem = FindListViewItemByPageNumber(firstPage);
                if (currentItem != null)
                {
                    currentItem.Selected = true;
                    currentItem.EnsureVisible();
                }
                else
                {
                    currentPage = firstPage;
                    pagesListView.SelectedItems.Clear();
                }
                ReloadRefreshCurrentPage();
            }
            // Update selection buttons state
            UpdateSelectionNavigationButtons();
        }

        private void SelectionPrevButton_Click(object sender, EventArgs e)
        {
            var pagesWithBlocks = GetPagesWithBlocks();
            if (!pagesWithBlocks.Any())
            {
                return;
            }
            int firstPage = pagesWithBlocks.First();
            int previousPage = pagesWithBlocks
                .Where(p => p < currentPage)
                .DefaultIfEmpty(firstPage)    // in case there is no previous, return firstPage or 0
                .LastOrDefault();
            if ((string)filterComboBox.SelectedItem == allComboItem)
            {
                pagesListView.Items[previousPage - 1].Selected = true;
                pagesListView.Items[previousPage - 1].EnsureVisible();
            } else
            {
                ListViewItem currentItem = FindListViewItemByPageNumber(previousPage);

                if (currentItem != null)
                {
                    currentItem.Selected = true;
                    currentItem.EnsureVisible();
                }
                else
                {
                    currentPage = previousPage;
                    pagesListView.SelectedItems.Clear();
                }
                ReloadRefreshCurrentPage();
            }

            // Update selection buttons state
            UpdateSelectionNavigationButtons();
        }

        private void SelectionNextButton_Click(object sender, EventArgs e)
        {
            var pagesWithBlocks = GetPagesWithBlocks();
            if (!pagesWithBlocks.Any())
            {
                return;
            }
            int lastPage = pagesWithBlocks.Last();
            int nextPage = pagesWithBlocks
                .Where(p => p > currentPage)
                .DefaultIfEmpty(lastPage)     // in case of no further pages, return lastPage or 0
                .FirstOrDefault();
            if ((string)filterComboBox.SelectedItem == allComboItem)
            {
                pagesListView.Items[nextPage - 1].Selected = true;
                pagesListView.Items[nextPage - 1].EnsureVisible();
            } else
            {
                ListViewItem currentItem = FindListViewItemByPageNumber(nextPage);
                if (currentItem != null)
                {
                    currentItem.Selected = true;
                    currentItem.EnsureVisible();
                }
                else
                {
                    currentPage = nextPage;
                    pagesListView.SelectedItems.Clear();
                }
                ReloadRefreshCurrentPage();
            }

            // Update selection buttons state
            UpdateSelectionNavigationButtons();
        }

        private void SelectionLastButton_Click(object sender, EventArgs e)
        {
            var pagesWithBlocks = GetPagesWithBlocks();
            if (!pagesWithBlocks.Any())
            {
                return;
            }
            int lastPage = pagesWithBlocks.Last();
            if ((string)filterComboBox.SelectedItem == allComboItem)
            {
                pagesListView.Items[lastPage - 1].Selected = true;
                pagesListView.Items[lastPage - 1].EnsureVisible();
            } else
            {
                ListViewItem currentItem = FindListViewItemByPageNumber(lastPage);
                if (currentItem != null)
                {
                    currentItem.Selected = true;
                    currentItem.EnsureVisible();
                }
                else
                {
                    currentPage = lastPage;
                    pagesListView.SelectedItems.Clear();
                }
                ReloadRefreshCurrentPage();
            }
            // Update selection buttons state
            UpdateSelectionNavigationButtons();
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            // Show dialog box with confirmation question
            string msqOutText = "";
            if (redactionBlocks.Count > 0 && projectWasChangedAfterLastSave)
            {
                msqOutText += Resources.Msg_Closing_UnsavedSelectionsPrefix;
            }
            msqOutText += Resources.Msg_Confirm_CloseApp;

            DialogResult result = MessageBox.Show(this,
                msqOutText,
                Resources.Title_Confirmation,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Exclamation,
                MessageBoxDefaultButton.Button2
            );

            // If user selects "No", cancel closing
            if (result == DialogResult.No)
            {
                e.Cancel = true;
            }

            if (!e.Cancel)
            {
                PersistCurrentPageToProjectFile();
            }

            if (inputPdfPath != "")
            {
                Properties.Settings.Default.LastPdfPath = inputPdfPath;
                Properties.Settings.Default.LastPapPath = "";
            }
            if (lastSavedProjectName != "")
            {
                Properties.Settings.Default.LastPapPath = lastSavedProjectName;
            }
            else if (inputProjectPath != "")
            {
                Properties.Settings.Default.LastPapPath = inputProjectPath;
            }
            Properties.Settings.Default.Save();
        }

        private void SaveProjectButton_Click(object sender, EventArgs e)
        {
            //if (redactionBlocks.Count == 0 && pagesToRemove.Count == 0 && textAnnotations.Count ==0)
            //{
            //    MessageBox.Show(this, "Select content or page to remove.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
            //    return;
            //}

            if (lastSavedProjectName == "" && inputProjectPath == "")
            {
                SaveProjectAs();
            }
            else
            {
                try
                {
                    if (projectWasChangedAfterLastSave)
                    {
                        var jsonSettings = new JsonSerializerSettings
                        {
                            Formatting = Formatting.Indented // prettier, indented format
                        };

                        // Serialize list to JSON string
                        if (lastSavedProjectName == "")
                        {
                            lastSavedProjectName = inputProjectPath;
                        }

                        ProjectData projectData = new ProjectData
                        {
                            RedactionBlocks = redactionBlocks,
                            PagesToRemove = pagesToRemove,
                            TextAnnotations = textAnnotations,
                            PageRotationOffsets = new Dictionary<int, int>(pageRotationOffsets),
                            FilePath = inputPdfPath,
                            CurrentPage = currentPage,
                            SignaturesMode = GetSignatureModeForProject(),
                            SignaturesToRemove = hasCustomSignatureSelection ? new List<string>(signaturesToRemove) : null
                        };

                        // Serialize list to JSON string
                        string json = JsonConvert.SerializeObject(projectData, jsonSettings);

                        File.WriteAllText(lastSavedProjectName, json);
                        projectWasChangedAfterLastSave = false;
                        saveProjectButton.Enabled = false;
                        saveProjectMenuItem.Enabled = false;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, string.Format(Resources.Err_Save, ex.Message), Resources.Title_Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ExitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void UpdateZoomButtons()
        {
            // If we have already reached maximum zoom, disable zoom in button
            zoomInButton.Enabled = (scaleFactor < maxScaleFactor);

            // If we are at minimum zoom, disable zoom out button
            zoomOutButton.Enabled = (scaleFactor > minScaleFactor);

            // If we are at minimum zoom, zoomMinButton makes no sense
            zoomMinButton.Enabled = (scaleFactor > minScaleFactor);

            // If we are at maximum zoom, zoomMaxButton makes no sense
            zoomMaxButton.Enabled = (scaleFactor < maxScaleFactor);
        }

        private void ZoomMinButton_Click(object sender, EventArgs e)
        {
            this.Cursor = Cursors.WaitCursor;
            minScaleButton = true;
            maxScaleButton = false;
            CalculateMinScaleFactor(currentPage);
            scaleFactor = minScaleFactor;
            DisplayPdfPage(currentPage);
            this.Cursor = Cursors.Default;
        }

        private void ZoomMaxButton_Click(object sender, EventArgs e)
        {
            this.Cursor = Cursors.WaitCursor;
            minScaleButton = false;
            maxScaleButton = true;
            CalculateMaxScaleFactor();
            scaleFactor = maxScaleFactor;
            DisplayPdfPage(currentPage);
            this.Cursor = Cursors.Default;
        }


        private void InstructionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                // Resolve help file by culture (UserGuide_en.pdf / UserGuide_pl.pdf) with fallback to UserGuide.pdf
                string culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName?.ToLowerInvariant() ?? "";
                string pdfHelpFile = Path.Combine(Application.StartupPath, $"UserGuide_{culture}.pdf");
                if (!File.Exists(pdfHelpFile))
                    pdfHelpFile = Path.Combine(Application.StartupPath, "UserGuide.pdf");

                // Check if file exists
                if (!File.Exists(pdfHelpFile))
                {
                    MessageBox.Show(this,
                        Resources.Msg_HelpNotFound,
                        Resources.Title_Error,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    return;
                }

                // Open PDF file in default system browser/editor
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = pdfHelpFile,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    string.Format(Resources.Msg_OpenPdfError, ex.Message),
                    Resources.Title_Error,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        private void HelpToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                // Resolve help file by culture (UserGuide_en.pdf / UserGuide_pl.pdf) with fallback to UserGuide.pdf
                string culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName?.ToLowerInvariant() ?? "";
                string pdfHelpFile = Path.Combine(Application.StartupPath, $"UserGuide_{culture}.pdf");
                if (!File.Exists(pdfHelpFile))
                    pdfHelpFile = Path.Combine(Application.StartupPath, "UserGuide.pdf");

                // Check if file exists
                if (!File.Exists(pdfHelpFile))
                {
                    MessageBox.Show(this, Resources.Msg_HelpNotFound, Resources.Title_Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Open PDF file in default system browser/editor
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = pdfHelpFile,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(Resources.Msg_OpenPdfError, ex.Message), Resources.Title_Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowLicenseToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                string licensePath = Path.Combine(Application.StartupPath, "LICENSE");
                if (!File.Exists(licensePath))
                {
                    MessageBox.Show(this,
                        Resources.Msg_LicenseNotFound,
                        Resources.Title_Error,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }
                var psi = new ProcessStartInfo { FileName = licensePath, UseShellExecute = true };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    string.Format(Resources.Msg_FileOpenError, ex.Message),
                    Resources.Title_Error,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void ThirdPartyNoticesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                string noticesPath = Path.Combine(Application.StartupPath, "THIRD-PARTY-NOTICES.md");
                if (!File.Exists(noticesPath))
                {
                    MessageBox.Show(this,
                        Resources.Msg_NoticesNotFound,
                        Resources.Title_Error,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }
                var psi = new ProcessStartInfo { FileName = noticesPath, UseShellExecute = true };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    string.Format(Resources.Msg_FileOpenError, ex.Message),
                    Resources.Title_Error,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void AboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Build "About application" message using localized resources
            var assembly = Assembly.GetExecutingAssembly();
            var companyAttribute = assembly.GetCustomAttribute<AssemblyCompanyAttribute>();
            var companyName = companyAttribute?.Company ?? string.Empty;
            var copyrightAttribute = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>();
            var copyright = Resources.ResourceManager.GetString("About_Copyright", Resources.Culture);
            if (string.IsNullOrWhiteSpace(copyright))
            {
                copyright = copyrightAttribute?.Copyright ?? string.Empty;
            }
            var descriptionAttribute = assembly.GetCustomAttribute<AssemblyDescriptionAttribute>();
            var description = Resources.ResourceManager.GetString("About_Description", Resources.Culture);
            if (string.IsNullOrWhiteSpace(description))
            {
                description = descriptionAttribute?.Description ?? string.Empty;
            }

            string aboutMessage = string.Format(
                Resources.About_Message,
                Branding.ProductName,
                fileVersion,
                companyName,
                copyright,
                description
            );

            var licenseLine = GetLicenseStatusLine();
            var updatesLine = GetUpdatesStatusLine();
            if (!string.IsNullOrWhiteSpace(licenseLine))
            {
                aboutMessage += Environment.NewLine + Environment.NewLine + licenseLine;
            }
            if (!string.IsNullOrWhiteSpace(updatesLine))
            {
                aboutMessage += Environment.NewLine + updatesLine;
            }

            // Display information in dialog box
            MessageBox.Show(this,
                aboutMessage,
                Resources.Menu_Help_About,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F11)
            {
                SetFullScreen(!isFullScreen);
                return true;
            }

            if (keyData == Keys.Escape && isFullScreen)
            {
                SetFullScreen(false);
                return true;
            }

            // Check if it's just Delete without modifiers
            if (keyData == Keys.Delete)
            {
                // 1) If focus is in searchTextBox, let textbox delete character:
                if (searchTextBox.Focused)
                    return false;  // we don't handle - pass on to control

                // 2) Otherwise treat Delete as "delete page":
                RemovePage();
                return true;     // handled, don't pass on
            }

            // remaining keys
            return base.ProcessCmdKey(ref msg, keyData);
        }


        private void PDFForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (pdf == null)
            {
                return;
            }

            switch (e.KeyCode)
            {

                case Keys.PageUp:
                    // Handle PageUp key:
                    PreviousPage(); // Method that goes to previous page
                    e.Handled = true;  // Indicates that event was handled
                    break;

                case Keys.PageDown:
                    // Handle PageDown key:
                    NextPage(); // Method that goes to next page
                    e.Handled = true;
                    break;

                case Keys.Home:
                    if (searchTextBox.Focused)
                    {
                        break;
                    }
                    FirstPage();
                    e.Handled = true;
                    break;

                case Keys.End:
                    if (searchTextBox.Focused)
                    {
                        break;
                    }
                    LastPage();
                    e.Handled = true;
                    break;
            }
        }

        private void OpenLastPdfProjectToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Get saved paths from Properties.Settings
            string lastPdf = Properties.Settings.Default.LastPdfPath;
            string lastPap = Properties.Settings.Default.LastPapPath;

            try
            {
                if (!string.IsNullOrEmpty(lastPdf) && File.Exists(lastPdf))
                {
                    // Load PDF
                    inputPdfPath = lastPdf;
                    inputProjectPath = "";
                    lastSavedProjectName = "";
                    LoadPdf();
                    string msg = string.Format(Resources.Msg_LoadedPdf, inputPdfPath);
                    if (!string.IsNullOrEmpty(lastPap) && File.Exists(lastPap))
                    {
                        //inputProjectPath = lastPap;
                        LoadRedactionBlocks(lastPap);
                        if (!string.IsNullOrEmpty(inputProjectPath))
                        {
                            msg += string.Format(Resources.Msg_LoadedProjectLine, inputProjectPath);
                        }
                    }
                    MessageBox.Show(this, $"{msg}.", Resources.Title_Info, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(this, Resources.Msg_CannotLoadRecentFiles, Resources.Title_Warning, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                    MessageBox.Show(this, string.Format(Resources.Err_LoadRecentFiles, ex.Message),
                        Resources.Title_Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private List<string> LoadRecentFiles()
        {
            string raw = Properties.Settings.Default.RecentFiles;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new List<string>();
            }

            try
            {
                var files = JsonConvert.DeserializeObject<List<string>>(raw);
                return files ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        private void SaveRecentFiles(List<string> files)
        {
            Properties.Settings.Default.RecentFiles = JsonConvert.SerializeObject(files);
            Properties.Settings.Default.Save();
        }

        private void AddRecentFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            string ext = Path.GetExtension(filePath);
            if (!string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(ext, ".pap", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string fullPath = Path.GetFullPath(filePath);
            var files = LoadRecentFiles();
            files.RemoveAll(path => string.Equals(path, fullPath, StringComparison.OrdinalIgnoreCase));
            files.Insert(0, fullPath);

            while (files.Count > RecentFilesLimit)
            {
                files.RemoveAt(files.Count - 1);
            }

            SaveRecentFiles(files);
            UpdateRecentFilesMenu();
        }

        private void RemoveRecentFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            var files = LoadRecentFiles();
            int removedCount = files.RemoveAll(path => string.Equals(path, filePath, StringComparison.OrdinalIgnoreCase));
            if (removedCount > 0)
            {
                SaveRecentFiles(files);
            }
        }

        private void UpdateRecentFilesMenu()
        {
            if (recentFilesMenuItem == null)
            {
                return;
            }

            var theme = CurrentTheme;
            recentFilesMenuItem.DropDownItems.Clear();

            var files = LoadRecentFiles();
            var uniqueFiles = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in files)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                string ext = Path.GetExtension(path);
                if (!string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(ext, ".pap", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (seen.Add(path))
                {
                    uniqueFiles.Add(path);
                }
            }

            if (uniqueFiles.Count == 0)
            {
                var emptyItem = new ToolStripMenuItem(Resources.Menu_RecentFiles_Empty);
                emptyItem.Enabled = false;
                emptyItem.ForeColor = theme.TextSecondaryColor;
                recentFilesMenuItem.DropDownItems.Add(emptyItem);
                recentFilesMenuItem.Enabled = false;
                return;
            }

            recentFilesMenuItem.Enabled = true;

            int count = Math.Min(uniqueFiles.Count, RecentFilesLimit);
            for (int i = 0; i < count; i++)
            {
                string path = uniqueFiles[i];
                string fileName = Path.GetFileName(path);
                var item = new ToolStripMenuItem($"{i + 1}. {fileName}");
                item.Tag = path;
                item.ToolTipText = path;
                item.AutoToolTip = true;
                if (!File.Exists(path))
                {
                    item.ForeColor = theme.TextSecondaryColor;
                }
                else
                {
                    item.ForeColor = theme.TextPrimaryColor;
                }
                item.Click += RecentFileMenuItem_Click;
                recentFilesMenuItem.DropDownItems.Add(item);
            }
        }

        private void RecentFilesMenuItem_DropDownOpening(object sender, EventArgs e)
        {
            UpdateRecentFilesMenu();
        }

        private void RecentFileMenuItem_Click(object sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem menuItem && menuItem.Tag is string filePath)
            {
                OpenRecentFile(filePath);
            }
        }

        private void OpenRecentFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            if (!File.Exists(filePath))
            {
                MessageBox.Show(this, Resources.Msg_CannotLoadRecentFiles, Resources.Title_Warning, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                RemoveRecentFile(filePath);
                UpdateRecentFilesMenu();
                return;
            }

            string ext = Path.GetExtension(filePath);
            if (string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                OpenPdfFromPath(filePath);
                return;
            }

            if (string.Equals(ext, ".pap", StringComparison.OrdinalIgnoreCase))
            {
                LoadRedactionBlocks(filePath);
            }
        }

        private void SignaturesRemoveRadioButton_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.LastSignaturesRemoveRadioButton = signaturesRemoveRadioButton.Checked;
            Properties.Settings.Default.Save();
            UpdateSignatureSelectionMenuState();
            if (!suppressSignatureModeChange && signaturesRemoveRadioButton.Checked)
            {
                projectWasChangedAfterLastSave = true;
                saveProjectButton.Enabled = true;
                saveProjectMenuItem.Enabled = true;
            }
        }

        private void SignaturesOriginalRadioButton_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.LastSignaturesOriginalRadioButton = signaturesOriginalRadioButton.Checked;
            Properties.Settings.Default.Save();
            UpdateSignatureSelectionMenuState();
            if (!suppressSignatureModeChange && signaturesOriginalRadioButton.Checked)
            {
                projectWasChangedAfterLastSave = true;
                saveProjectButton.Enabled = true;
                saveProjectMenuItem.Enabled = true;
            }
        }

        private void SignaturesReportRadioButton_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.LastSignaturesRaportRadioButton = signaturesReportRadioButton.Checked;
            Properties.Settings.Default.Save();
            UpdateSignatureSelectionMenuState();
            if (!suppressSignatureModeChange && signaturesReportRadioButton.Checked)
            {
                projectWasChangedAfterLastSave = true;
                saveProjectButton.Enabled = true;
                saveProjectMenuItem.Enabled = true;
            }
        }

        private void UpdateSignatureSelectionMenuState()
        {
            if (selectSignaturesToRemoveMenuItem == null)
            {
                return;
            }

            selectSignaturesToRemoveMenuItem.Enabled = signaturesRemoveRadioButton.Checked && signatures.Count > 0;
        }

        private void SelectSignaturesToRemoveMenuItem_Click(object sender, EventArgs e)
        {
            if (!signaturesRemoveRadioButton.Checked || signatures.Count == 0)
            {
                return;
            }

            IEnumerable<string> preselected = hasCustomSignatureSelection ? signaturesToRemove : null;
            using (SelectSignaturesDialog dlg = new SelectSignaturesDialog(signatures, preselected))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    signaturesToRemove = dlg.SelectedFieldNames;
                    hasCustomSignatureSelection = true;
                    projectWasChangedAfterLastSave = true;
                    saveProjectButton.Enabled = true;
                    saveProjectMenuItem.Enabled = true;
                }
            }
        }

        private void SyncSignatureSelectionWithAvailableSignatures()
        {
            if (!hasCustomSignatureSelection)
            {
                return;
            }

            var existing = new HashSet<string>(
                signatures.Select(sig => sig.FieldName ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);

            signaturesToRemove = signaturesToRemove
                .Where(name => !string.IsNullOrWhiteSpace(name) && existing.Contains(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private string GetSignatureModeForProject()
        {
            if (signaturesRemoveRadioButton.Checked)
            {
                return SignatureModeRemove;
            }
            if (signaturesReportRadioButton.Checked)
            {
                return SignatureModeReport;
            }
            return SignatureModeOriginal;
        }

        private void ApplySignatureModeFromProject(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
            {
                return;
            }

            suppressSignatureModeChange = true;
            try
            {
                switch (mode.Trim().ToLowerInvariant())
                {
                    case SignatureModeRemove:
                        signaturesRemoveRadioButton.Checked = true;
                        break;
                    case SignatureModeReport:
                        signaturesReportRadioButton.Checked = true;
                        break;
                    default:
                        signaturesOriginalRadioButton.Checked = true;
                        break;
                }
            }
            finally
            {
                suppressSignatureModeChange = false;
            }
        }

        private void ColorCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.LastColorCheckBoxState = colorCheckBox.Checked;
            Properties.Settings.Default.Save();
        }

        private void OpenSavedPdfCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.LastOpenSavedPDFCheckBoxState = openSavedPDFCheckBox.Checked;
            Properties.Settings.Default.Save();
        }
        

        private void SafeModeCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            //Properties.Settings.Default.LastSafeModeCheckBoxState = safeModeCheckBox.Checked;
            //Properties.Settings.Default.Save();
            if (safeModeCheckBox.Enabled)
            {
                renderTimer.Stop();
                renderTimer.Start();
            }
        }


        private string DetectMainFilterForPage(iText.Kernel.Pdf.PdfPage page)
        {
            var resources = page.GetResources();
            if (resources == null)
                return null;

            // Get XObject dictionary
            var xObjects = resources.GetResource(PdfName.XObject);
            if (xObjects == null)
                return null; // no XObjects

            foreach (var xObjName in xObjects.KeySet())
            {
                // Read object's PdfStream
                var xObjStream = xObjects.GetAsStream(xObjName);
                if (xObjStream == null)
                    continue; // can be e.g. Form XObject

                // Check if this is an image at all
                var subtype = xObjStream.GetAsName(PdfName.Subtype);
                if (subtype == null || !subtype.Equals(PdfName.Image))
                    continue; // not Image (may be other XObject type)

                // Get filter (can be PdfName, PdfArray or none)
                var filterObj = xObjStream.Get(PdfName.Filter);
                if (filterObj == null)
                {
                    // No filter -> raw data (sometimes "RawData")
                    return null;
                }

                // If filter is single PdfName...
                if (filterObj.IsName())
                {
                    var singleFilterName = (PdfName)filterObj;
                    return singleFilterName.GetValue();
                    // e.g. "CCITTFaxDecode", "DCTDecode", "FlateDecode", "JBIG2Decode", etc.
                }
                // If filter is array (PdfArray) - in PDF there can be multiple filters
                else if (filterObj.IsArray())
                {
                    var arr = (PdfArray)filterObj;
                    if (arr.Size() > 0)
                    {
                        // Take first filter (or can iterate over all)
                        if (arr.Get(0) is PdfName firstFilter)
                        {
                            return firstFilter.GetValue();
                        }
                    }
                }
            }

            // If we didn't find any image or didn't detect filter:
            return null;
        }

        public bool ReencodePdfKeepOriginalCompression(string inputPdf, string outputPdf)
        {
            bool isReencoded = false;
            reencodedPages.Clear();

            var props = new ReaderProperties();
            if (!string.IsNullOrEmpty(userPassword))
            {
                props.SetPassword(System.Text.Encoding.UTF8.GetBytes(userPassword));
            }

            

            using (PdfReader reader = new PdfReader(inputPdf, props).SetUnethicalReading(Properties.Settings.Default.IgnorePdfRestrictions))
            using (iText.Kernel.Pdf.PdfDocument srcDoc = new iText.Kernel.Pdf.PdfDocument(reader))
            {
                int numberOfPages = srcDoc.GetNumberOfPages();

                // Check reencoding mode – if disabled, return false.
                if (!isReencodingMode)
                    return false;

                // Get filter for each page
                List<string> pageFilters = new List<string>();
                for (int i = 1; i <= numberOfPages; i++)
                {
                    string filterForPage = DetectMainFilterForPage(srcDoc.GetPage(i));
                    pageFilters.Add(filterForPage);
                }

                // If any page doesn't have specified filter – reencoding is not performed
                //if (!(pageFilters.Any(pf => pf != null) || openSavedPDFCheckBox.Checked))
                //    return false;

                // Load PDF with PDFiumSharp to render pages
                var pdfiumDoc = new PDFiumSharp.PdfDocument(inputPdf, userPassword);

                using (PdfWriter writer = new PdfWriter(outputPdf))
                using (iText.Kernel.Pdf.PdfDocument destDoc = new iText.Kernel.Pdf.PdfDocument(writer))
                {
                    for (int i = 0; i < numberOfPages; i++)
                    {
                        int pageNumber = i + 1;

                        // If there are no redaction blocks for given page – copy page without reencoding
                        if (!redactionBlocks.Any(b => b.PageNumber == pageNumber))
                        {
                            srcDoc.CopyPagesTo(pageNumber, pageNumber, destDoc);
                            continue;
                        }
                        if (!(safeModeCheckBox.Checked || pdfCleanUpToolError))
                        {
                            // Check if there are text objects on page.
                            string extractedText = iText.Kernel.Pdf.Canvas.Parser.PdfTextExtractor.GetTextFromPage(srcDoc.GetPage(pageNumber));
                            if (!string.IsNullOrWhiteSpace(extractedText))
                            {
                                srcDoc.CopyPagesTo(pageNumber, pageNumber, destDoc);
                                continue;
                            }

                            // Perform re-encoding only for pages encoded as "CCITTFaxDecode"
                            if (pageFilters[i] != "CCITTFaxDecode")
                            {
                                srcDoc.CopyPagesTo(pageNumber, pageNumber, destDoc);
                                continue;
                            }
                        }


                        // For pages meeting conditions perform reencoding.

                        isReencoded = true;
                        reencodedPages.Add(pageNumber);
                        // Load page from PDFiumSharp:
                        var pdfiumPage = pdfiumDoc.Pages[i];
                        double pageWidthPoints = pdfiumPage.Width;
                        double pageHeightPoints = pdfiumPage.Height;
                        if (DebugLogEnabled)
                        {
                            var srcPage = srcDoc.GetPage(pageNumber);
                            var size = srcPage.GetPageSize();
                            LogDebug($"Reencode page={pageNumber} filter={pageFilters[i] ?? "none"} rotate={srcPage.GetRotation()} itext={size.GetWidth()}x{size.GetHeight()} pdfium={pageWidthPoints}x{pageHeightPoints} safeMode={safeModeCheckBox.Checked} cleanupError={pdfCleanUpToolError}");
                        }

                        int dpi = reencodingDPI;
                        int bmpWidth = (int)(pageWidthPoints * dpi / 72.0);
                        int bmpHeight = (int)(pageHeightPoints * dpi / 72.0);
                        if (DebugLogEnabled)
                        {
                            LogDebug($"Reencode bitmap page={pageNumber} dpi={dpi} size={bmpWidth}x{bmpHeight}");
                        }

                        using (PDFiumBitmap pdfiumBmp = new PDFiumBitmap(bmpWidth, bmpHeight, true))
                        {
                            pdfiumBmp.FillRectangle(0, 0, bmpWidth, bmpHeight, 0xFFFFFFFF);
                            pdfiumPage.Render(renderTarget: pdfiumBmp, flags: PDFiumSharp.Enums.RenderingFlags.Annotations);

                            Bitmap bmp;
                            using (MemoryStream ms = new MemoryStream())
                            {
                                pdfiumBmp.Save(ms);
                                ms.Position = 0;
                                bmp = (Bitmap)System.Drawing.Image.FromStream(ms);
                            }

                            byte[] encodedBytes;
                            // For "CCITTFaxDecode" perform reencoding using TIFF G4.
                            if (pageFilters[i] == "CCITTFaxDecode")
                            {
                                using (var oneBit = ConvertTo1Bit(bmp, 128))
                                {
                                    encodedBytes = EncodeToTiffG4(oneBit);
                                }
                            } else if (pageFilters[i] == "FlateDecode")
                            {
                                encodedBytes = EncodeToPng(bmp);
                            } else
                            {
                                encodedBytes = EncodeToJpeg(bmp, quality: 90L);
                            }

                            bmp.Dispose();

                            var pageSize = new iText.Kernel.Geom.PageSize((float)pageWidthPoints, (float)pageHeightPoints);
                            var newPage = destDoc.AddNewPage(pageSize);
                            var pdfCanvas = new iText.Kernel.Pdf.Canvas.PdfCanvas(newPage);
                            using (var layoutCanvas = new iText.Layout.Canvas(pdfCanvas, pageSize))
                            {
                                var imgData = iText.IO.Image.ImageDataFactory.Create(encodedBytes);
                                var image = new iText.Layout.Element.Image(imgData)
                                    .ScaleAbsolute((float)pageWidthPoints, (float)pageHeightPoints)
                                    .SetFixedPosition(0, 0);
                                layoutCanvas.Add(image);
                            }
                        }
                    }
                }

                return isReencoded;
            }
        }


        private Bitmap ConvertTo1Bit(Bitmap img, byte threshold)
        {
            int width = img.Width;
            int height = img.Height;

            // Create 1-bit image
            Bitmap bmp = new Bitmap(width, height, PixelFormat.Format1bppIndexed);
            bmp.SetResolution(img.HorizontalResolution, img.VerticalResolution);

            // Lock bits of the original image
            DrawingRectangle rect = new DrawingRectangle(0, 0, width, height);
            BitmapData imgData = img.LockBits(rect, ImageLockMode.ReadOnly, img.PixelFormat);
            BitmapData bmpData = bmp.LockBits(rect, ImageLockMode.WriteOnly, bmp.PixelFormat);

            int imgBytesPerPixel = DrawingImage.GetPixelFormatSize(img.PixelFormat) / 8;
            int imgStride = imgData.Stride;
            int bmpStride = bmpData.Stride;

            byte[] imgBuffer = new byte[imgStride * height];
            byte[] bmpBuffer = new byte[bmpStride * height];

            // Copy image data into buffer
            System.Runtime.InteropServices.Marshal.Copy(imgData.Scan0, imgBuffer, 0, imgBuffer.Length);

            for (int y = 0; y < height; y++)
            {
                int imgRow = y * imgStride;
                int bmpRow = y * bmpStride;

                for (int x = 0; x < width; x++)
                {
                    int imgIndex = imgRow + x * imgBytesPerPixel;

                    // Get pixel brightness (conversion to grayscale)
                    byte brightness = (byte)(0.299 * imgBuffer[imgIndex + 2] + 0.587 * imgBuffer[imgIndex + 1] + 0.114 * imgBuffer[imgIndex]);

                    // Reverse condition
                    if (brightness < threshold)
                    {
                    // Dark pixel — set to white (default is white, so we do nothing)
                    }
                    else
                    {
                        // Bright pixel — set to black
                        bmpBuffer[bmpRow + x / 8] |= (byte)(0x80 >> (x % 8));
                    }
                }
            }

            // Copy buffer data back to image
            System.Runtime.InteropServices.Marshal.Copy(bmpBuffer, 0, bmpData.Scan0, bmpBuffer.Length);

            img.UnlockBits(imgData);
            bmp.UnlockBits(bmpData);

            return bmp;
        }


        private byte[] EncodeToTiffG4(Bitmap oneBitBmp)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                // Find the TIFF codec
                ImageCodecInfo tiffCodec = ImageCodecInfo.GetImageDecoders()
                    .FirstOrDefault(codec => codec.FormatID == ImageFormat.Tiff.Guid);

                if (tiffCodec == null)
                    throw new InvalidOperationException("TIFF codec not found on the system.");

                // Set compression parameter to CCITT Group 4
                var encoderParams = new EncoderParameters(2);
                encoderParams.Param[0] = new EncoderParameter(
                    System.Drawing.Imaging.Encoder.Compression,
                    (long)EncoderValue.CompressionCCITT4
                );
                // Set black and white color mode
                encoderParams.Param[1] = new EncoderParameter(Encoder.ColorDepth, 1L);

                // Write to MemoryStream
                oneBitBmp.Save(ms, tiffCodec, encoderParams);

                return ms.ToArray(); // Return TIFF bytes (CCITT Group 4)
            }
        }

        private byte[] EncodeToJpeg(Bitmap bmp, long quality = 90L)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                // Find the JPEG codec
                var jpegCodec = ImageCodecInfo.GetImageDecoders()
                    .FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);

                if (jpegCodec == null)
                    throw new InvalidOperationException("JPEG codec not found on the system.");

                // Compression parameters
                var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(
                    System.Drawing.Imaging.Encoder.Quality,
                    quality
                );

                using (Bitmap temp24 = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format24bppRgb))
                {
                    temp24.SetResolution(bmp.HorizontalResolution, bmp.VerticalResolution);
                    using (Graphics g = Graphics.FromImage(temp24))
                    {
                        g.DrawImage(bmp, 0, 0);
                    }
                    temp24.Save(ms, jpegCodec, encoderParams);
                }

                return ms.ToArray(); // bajty JPEG
            }
        }

        private byte[] EncodeToPng(Bitmap bmp)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (Bitmap temp24 = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format24bppRgb))
                {
                    temp24.SetResolution(bmp.HorizontalResolution, bmp.VerticalResolution);
                    using (Graphics g = Graphics.FromImage(temp24))
                    {
                        g.DrawImage(bmp, 0, 0);
                    }
                    temp24.Save(ms, ImageFormat.Png);
                }

                return ms.ToArray();
            }
        }

        private void UpdateWindowTitle()
        {

            string titleText = $"{Branding.ProductName} - v.{fileVersion}";
            string demoSuffix = GetDemoTitleSuffix();
            if (!string.IsNullOrWhiteSpace(demoSuffix))
            {
                titleText += demoSuffix;
            }
            if (inputPdfPath != "")
            {
                string pdfFileName = Path.GetFileName(inputPdfPath);
                titleText += $"              pdf: [{pdfFileName}]";
            }
            string papText = "";
            if (inputProjectPath != "")
            {
                string papFileName = Path.GetFileName(inputProjectPath);
                papText = $"        projekt: [{papFileName}]";
            }

            if (lastSavedProjectName != "")
            {
                string papFileName = Path.GetFileName(lastSavedProjectName);
                papText = $"        projekt: [{papFileName}]";
            }

            if (papText != "")
            {
                titleText += papText;
            }

            this.Text = titleText;
        }

        private static string GetDemoTitleSuffix()
        {
            var info = LicenseManager.Current;
            if (info == null || !info.IsSignatureValid || info.Payload == null)
            {
                return string.Empty;
            }

            if (!string.Equals(info.Payload.Edition, "demo", StringComparison.OrdinalIgnoreCase)
                && !LicenseManager.IsUpdateOutOfRangeForCurrentVersion)
            {
                return string.Empty;
            }

            if (LicenseManager.IsUpdateOutOfRangeForCurrentVersion)
            {
                return IsPolishCulture()
                    ? " [WERSJA DEMO: brak licencji na nowsze wersje]"
                    : " [DEMO: updates not licensed]";
            }

            var demoUntil = ParseLicenseDate(info.Payload.DemoUntil);
            if (!demoUntil.HasValue)
            {
                return IsPolishCulture() ? " [WERSJA DEMO]" : " [DEMO VERSION]";
            }

            var daysLeft = (int)Math.Ceiling((demoUntil.Value.Date - DateTime.UtcNow.Date).TotalDays);
            if (daysLeft >= 0)
            {
                return IsPolishCulture()
                    ? $" [WERSJA DEMO: {daysLeft} dni]"
                    : $" [DEMO: {daysLeft} days left]";
            }

            return IsPolishCulture()
                ? $" [WERSJA DEMO: wygasla {demoUntil:yyyy-MM-dd}]"
                : $" [DEMO: expired on {demoUntil:yyyy-MM-dd}]";
        }

        private static string GetLicenseStatusLine()
        {
            var info = LicenseManager.Current;
            if (info == null)
            {
                return IsPolishCulture() ? "Status licencji: brak" : "License status: missing";
            }

            if (!info.IsSignatureValid)
            {
                return IsPolishCulture() ? "Status licencji: nieprawidlowa" : "License status: invalid";
            }

            if (info.Payload == null)
            {
                return IsPolishCulture() ? "Status licencji: brak danych" : "License status: no data";
            }

            if (LicenseManager.IsUpdateOutOfRangeForCurrentVersion)
            {
                return IsPolishCulture()
                    ? "Status licencji: DEMO (brak licencji na nowsze wersje)"
                    : "License status: DEMO (updates not licensed)";
            }

            if (string.Equals(info.Payload.Edition, "demo", StringComparison.OrdinalIgnoreCase))
            {
                var demoUntil = ParseLicenseDate(info.Payload.DemoUntil);
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
                    ? $"Status licencji: DEMO (wygasla {demoUntil:yyyy-MM-dd})"
                    : $"License status: DEMO (expired {demoUntil:yyyy-MM-dd})";
            }

            return IsPolishCulture() ? "Status licencji: PRO" : "License status: PRO";
        }

        private static string GetUpdatesStatusLine()
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
                ? $"Aktualizacje: wygasly ({updatesUntil:yyyy-MM-dd})"
                : $"Updates: expired ({updatesUntil:yyyy-MM-dd})";
        }

        private static DateTime? ParseLicenseDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out DateTime exact))
            {
                return DateTime.SpecifyKind(exact, DateTimeKind.Utc);
            }

            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTime parsed))
            {
                return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }

            return null;
        }

        private static bool IsPolishCulture()
        {
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pl";
        }


        public void ExtractSignatures()
        {
            signatures.Clear();
            var props = new ReaderProperties();
            if (!string.IsNullOrEmpty(userPassword))
            {
                props.SetPassword(System.Text.Encoding.UTF8.GetBytes(userPassword));
            }
            using (PdfReader reader = new PdfReader(inputPdfPath, props).SetUnethicalReading(Properties.Settings.Default.IgnorePdfRestrictions))
            using (iText.Kernel.Pdf.PdfDocument pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader))
            {
                // Tool for handling signatures
                SignatureUtil signUtil = new SignatureUtil(pdfDoc);

                // Gets list of field names where signature actually exists
                IList<string> sigNames = signUtil.GetSignatureNames();

                foreach (string name in sigNames)
                {
                    PdfPKCS7 pkcs7 = signUtil.ReadSignatureData(name);

                    DateTime signDate = pkcs7.GetSignDate().ToLocalTime();

                    IX509Certificate cert = pkcs7.GetSigningCertificate();
                    var subject = CertificateInfo.GetSubjectFields(cert);

                    string certCn = subject.GetField("CN") ?? "";
                    string certT = subject.GetField("T") ?? "";

                    signatures.Add(new SignatureInfo
                    {
                        FieldName = name,
                        SignerName = certCn,
                        SignerTitle = certT,
                        SignDate = signDate
                    });
                }
            }

            groupBoxSignatures.Enabled = (signatures.Count > 0);
            if (signatures.Count == 0)
            {
                signaturesToRemove.Clear();
                hasCustomSignatureSelection = false;
            }
            SyncSignatureSelectionWithAvailableSignatures();
            UpdateSignatureSelectionMenuState();
        }

        private void RemovePage()
        {
            if (numPages == 1)
            {
                MessageBox.Show(this, Resources.Err_TooFewPages, Resources.Title_Error, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }

            bool isMarked = pagesToRemove.Contains(currentPage);

            PageItemStatus status = allPageStatuses[currentPage - 1];
            if (isMarked)
            {
                pagesToRemove.Remove(currentPage);
                UpdateRemovePageButtonVisual(false);
                status.MarkedForDeletion = false;
            }
            else
            {
                UpdateRemovePageButtonVisual(true);
                pagesToRemove.Add(currentPage);
                status.MarkedForDeletion = true;
            }

            if ((string)filterComboBox.SelectedItem == allComboItem)
            {
                // only refresh this row
                ListViewItem currentItem = pagesListView.Items[currentPage - 1];
                UpdateItemTag(currentItem, currentPage, status.HasSelections, status.HasSearchResults, status.MarkedForDeletion, status.HasTextAnnotations);
            }
            else
            {
                // rebuild list by filter
                ApplyFilter((string)filterComboBox.SelectedItem);
            }


            pagesListView.Invalidate();
            projectWasChangedAfterLastSave = true;
            saveProjectButton.Enabled = true;
            saveProjectMenuItem.Enabled = true;
        }

        private void RemovePageButton_Click(object sender, EventArgs e)
        {
            RemovePage();
        }


        private byte[] ExtractPageToBytes(int pageNumber)
        {
            // Check if input file exists
            if (!File.Exists(inputPdfPath))
            {
                throw new FileNotFoundException("Input file does not exist", inputPdfPath);
            }

            using (MemoryStream outputStream = new MemoryStream())
            {
                // Open input PDF file
                var props = new ReaderProperties();
                if (!string.IsNullOrEmpty(userPassword))
                {
                    props.SetPassword(System.Text.Encoding.UTF8.GetBytes(userPassword));
                }
                using (iText.Kernel.Pdf.PdfDocument pdfDoc = new iText.Kernel.Pdf.PdfDocument(new PdfReader(inputPdfPath, props).SetUnethicalReading(Properties.Settings.Default.IgnorePdfRestrictions)))
                {
                    // Check if page number is correct
                    if (pageNumber < 1 || pageNumber > pdfDoc.GetNumberOfPages())
                    {
                        throw new ArgumentOutOfRangeException(nameof(pageNumber), $"Page number must be between 1 and {pdfDoc.GetNumberOfPages()}");
                    }

                    // Create new PDF document for page
                    using (iText.Kernel.Pdf.PdfDocument extractedPageDoc = new iText.Kernel.Pdf.PdfDocument(new PdfWriter(outputStream)))
                    {
                        // Copy selected page to new document
                        pdfDoc.CopyPagesTo(pageNumber, pageNumber, extractedPageDoc);

                        // Close the new PDF document
                        extractedPageDoc.Close();
                    }
                }

                // Get bytes from memory stream
                return outputStream.ToArray();
            }
        }

        private void ShowRedactPreview()
        {
            var blocksForThisPage = redactionBlocks.Where(b => b.PageNumber == currentPage);
            if (blocksForThisPage.ToList().Any())
            {
                this.Cursor = Cursors.WaitCursor;
                if (CurrentPageContainsText())
                {
                    try
                    {
                        pdfViewer.Image = RenderCurrentPageWithPdfCleanUp();
                    }
                    catch (Exception)
                    {
                        pdfViewer.Image = RenderOriginalPage(currentPage);
                        pdfCleanUpToolError = true;
                    }

                }
                else
                {
                    pdfViewer.Image = RenderCurrentPageWithSelections();
                }
                this.Cursor = Cursors.Default;
            }
            else
            {
                pdfViewer.Image = RenderOriginalPage(currentPage);
            }
        }

        private void RemovePageRangeButton_Click(object sender, EventArgs e)
        {
            if (numPages == 1)
            {
                MessageBox.Show(this, Resources.Err_TooFewPages, Resources.Title_Error, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }

            // Display dialog with page range, where maximum range is based on numPages variable.
            using (DeletePagesDialog dlg = new DeletePagesDialog(numPages))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    PageItemStatus status;
                    int step = dlg.StartPage;
                    if (dlg.ApplyDeletion)
                    {
                        // Received page range for removal: dlg.StartPage to dlg.EndPage.
                        // Here you can save this range to variable or perform appropriate action.
                        //MessageBox.Show($"Page range for removal: {dlg.StartPage} - {dlg.EndPage}");
                        // For example, you can add these pages to pagesToRemove set:
                        for (int i = dlg.StartPage; i <= dlg.EndPage; i++)
                        {
                            
                            
                            if (step == i)
                            {
                                step += dlg.Step;
                                if (i == currentPage)
                                {
                                    UpdateRemovePageButtonVisual(true);
                                }

                                //ListViewItem item = pagesListView.Items[i - 1];
                                //int pageNumber = ((PageItemStatus)item.Tag).PageNumber;
                                //bool hasSelections = ((PageItemStatus)item.Tag).HasSelections;
                                //bool hasSearchResults = ((PageItemStatus)item.Tag).HasSearchResults;
                                //bool hasTextAnnotations = ((PageItemStatus)item.Tag).HasTextAnnotations;
                                //UpdateItemTag(item, pageNumber, hasSelections, hasSearchResults, true, hasTextAnnotations);

                                status = allPageStatuses[i - 1];
                                status.MarkedForDeletion = true;
                                int pageNumber = status.PageNumber;
                                if ((string)filterComboBox.SelectedItem == allComboItem)
                                {
                                    // only refresh this row
                                    ListViewItem currentItem = pagesListView.Items[i - 1];
                                    UpdateItemTag(currentItem, pageNumber, status.HasSelections, status.HasSearchResults, status.MarkedForDeletion, status.HasTextAnnotations);
                                    //pagesListView.Invalidate(currentItem.Bounds);
                                }
                                pagesToRemove.Add(i);
                            }

                        }
                        // Aktualizacja interfejsu – np. oznaczenie stron w listView itp.

                    }
                    else
                    {
                        for (int i = dlg.StartPage; i <= dlg.EndPage; i++)
                        {
                            if (i == currentPage)
                            {
                                UpdateRemovePageButtonVisual(false);
                            }

                            status = allPageStatuses[i - 1];
                            int pageNumber = status.PageNumber;

                            //ListViewItem item = pagesListView.Items[i - 1];
                            //int pageNumber = ((PageItemStatus)item.Tag).PageNumber;
                            //bool hasSelections = ((PageItemStatus)item.Tag).HasSelections;
                            
                            status.MarkedForDeletion = false;
                            if ((string)filterComboBox.SelectedItem == allComboItem)
                            {
                                // only refresh this row
                                ListViewItem currentItem = pagesListView.Items[i - 1];
                                UpdateItemTag(currentItem, pageNumber, status.HasSelections, status.HasSearchResults, status.MarkedForDeletion, status.HasTextAnnotations);
                                
                            }
                            pagesToRemove.Remove(i);
                        }

                        // User chose not to delete pages – handle accordingly if needed
                    }
                    if ((string)filterComboBox.SelectedItem != allComboItem)
                    {
                        ApplyFilter((string)filterComboBox.SelectedItem);
                    }
                        
                    pagesListView.Invalidate();
                }
            }
        }

        private void CenterSearchResult(iText.Kernel.Geom.Rectangle resultRect, int pageNumber)
        {
            // Convert PDF coordinates to control coordinates (e.g. image or panel)
            int rotation = GetEffectiveRotationDegrees(pageNumber);
            RectangleF pdfCoordinates = ConvertToPdfCoordinates(new RectangleF(resultRect.GetX(), resultRect.GetY(), resultRect.GetWidth(), resultRect.GetHeight()), pageNumber, rotation);

            // Scale coordinates if using zoom
            RectangleF scaledRect = new RectangleF(pdfCoordinates.X * scaleFactor,
                                                   pdfCoordinates.Y * scaleFactor,
                                                   pdfCoordinates.Width * scaleFactor,
                                                   pdfCoordinates.Height * scaleFactor);

            // Calculate center point of selection
            float centerX = scaledRect.X + scaledRect.Width / 2;
            float centerY = scaledRect.Y + scaledRect.Height / 2;

            ZoomPanel panel = mainAppSplitContainer.Panel2.Controls[0] as ZoomPanel;

            // Get the visible area size of the control (e.g., Panel.ClientSize)
            Size clientSize = panel.ClientSize;

            // Calculate new scroll position: we want point centerX, centerY to be in center of control
            int scrollX = (int)(centerX - clientSize.Width / 2);
            int scrollY = (int)(centerY - clientSize.Height / 2);

            // Set AutoScrollPosition.
            // Note: AutoScrollPosition property interprets values as negative offsets.
            panel.AutoScrollPosition = new Point(scrollX, scrollY);
        }

        private ListViewItem FindListViewItemByPageNumber(int pageNumber)
        {
            return pagesListView.Items
                .Cast<ListViewItem>()
                .FirstOrDefault(item => ((PageItemStatus)item.Tag).PageNumber == pageNumber);
        }

        // Method navigates to specified instance (and changes page if necessary)
        private void GoToLocation(int index)
        {
            if (index < 0 || index >= searchLocations.Count)
                return;

            var location = searchLocations[index];
            if (location.PageNumber != currentPage)
            {
                // Zmiana strony
                currentPage = location.PageNumber;
                if ((string)filterComboBox.SelectedItem == allComboItem)
                {
                    pagesListView.Items[currentPage - 1].Selected = true;
                    pagesListView.Items[currentPage - 1].EnsureVisible();
                } else
                {
                    ListViewItem currentItem = FindListViewItemByPageNumber(currentPage);
                    if (currentItem != null)
                    {
                        currentItem.Selected = true;
                        currentItem.EnsureVisible();
                    }
                    else
                    {
                        pagesListView.SelectedItems.Clear();
                    }
                    ReloadRefreshCurrentPage();
                }

                // Update button states
                UpdateNavigationButtons(currentPage);
            }
            CenterSearchResult(location.Rect, location.PageNumber);
            currentLocationIndex = index;
            // Refresh drawing to change highlighting
            pdfViewer.Invalidate();
            UpdateSearchNavigationButtons();
        }


        private async void SearchText()
        {
            string search = searchTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(search))
            {
                ClearSearchResult();
                //this.Cursor = Cursors.WaitCursor;
                groupBoxSearch.Enabled = false;
                searchLocations = await Task.Run(() => PdfTextSearcher.FindTextLocations(inputPdfPath, search, false, userPassword, this));
                groupBoxSearch.Enabled = true;
                searchTextBox.SelectAll();
                searchTextBox.Focus();
                searchResultLabel.Text = $"znaleziono: {searchLocations.Count}";                
                foreach (var itemSl in searchLocations)
                {
                    if (itemSl is TextLocation searchLocation)
                    {
                        int pageNumber = searchLocation.PageNumber;
                        //UpdateItemTag(item, pageNumber, hasSelections, true, markedForDeletion, hasTextAnnotations);
                        

                        PageItemStatus status = allPageStatuses[pageNumber - 1];
                        status.HasSearchResults = true;

                        if ((string)filterComboBox.SelectedItem == allComboItem)
                        {
                            ListViewItem currentItem = pagesListView.Items[pageNumber - 1];
                            UpdateItemTag(currentItem, pageNumber, status.HasSelections, status.HasSearchResults, status.MarkedForDeletion, status.HasTextAnnotations);
                            pagesListView.Invalidate(currentItem.Bounds);
                        }

                    }

                    

                }

                if ((string)filterComboBox.SelectedItem != allComboItem)
                {
                    // rebuild list according to filter
                    ApplyFilter((string)filterComboBox.SelectedItem);
                }

                pdfViewer.Invalidate();
                if (searchLocations.Count == 0)
                {
                    MessageBox.Show(this, string.Format(Resources.Msg_SearchNotFound, search), Resources.Title_Info, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                else
                {
                    GoToLocation(0);
                }
            }
            searchTextBox.SelectAll();
            UpdateSearchNavigationButtons();
        }

        private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                SearchText();
                e.SuppressKeyPress = true;
            }
        }

        private void SearchTextBox_Click(object sender, EventArgs e)
        {
            // Select all text in the field
            searchTextBox.SelectAll();
        }

        private void SearchFirstButton_Click(object sender, EventArgs e)
        {
            if (searchLocations != null && searchLocations.Count > 0)
            {
                GoToLocation(0);
            }
        }

        private void SearchPrevButton_Click(object sender, EventArgs e)
        {
            if (searchLocations == null || searchLocations.Count == 0)
                return;

            // If the current result is on page currentPage...
            if (searchLocations[currentLocationIndex].PageNumber == currentPage)
            {
                // Find the first result index on the current page.
                int firstIndexOnCurrentPage = searchLocations.FindIndex(loc => loc.PageNumber == currentPage);
                // If the active result is not the first on the current page, go to the previous result.
                if (currentLocationIndex > firstIndexOnCurrentPage)
                {
                    GoToLocation(currentLocationIndex - 1);
                    return;
                }
                // Otherwise the active result is the first from the current page -
                // so we go to the nearest result from previous pages.
            }

            // If there are no results on the current page or the active result is the first on that page,
            // we search for the nearest result from previous pages (i.e., whose PageNumber < currentPage)
            int prevIndex = -1;
            for (int i = searchLocations.Count - 1; i >= 0; i--)
            {
                if (searchLocations[i].PageNumber < currentPage)
                {
                    prevIndex = i;
                    break;
                }
            }

            if (prevIndex != -1)
            {
                GoToLocation(prevIndex);
            }
        }

        private void SearchNextButton_Click(object sender, EventArgs e)
        {
            if (searchLocations == null || searchLocations.Count == 0)
                return;

            // If the current result is on the current page
            if (searchLocations[currentLocationIndex].PageNumber == currentPage)
            {
                // Find the index of the last result on the current page
                int lastIndexOnCurrentPage = searchLocations.FindLastIndex(loc => loc.PageNumber == currentPage);
                // If the active result is not the last on the current page, go to the next result on that page.
                if (currentLocationIndex < lastIndexOnCurrentPage)
                {
                    GoToLocation(currentLocationIndex + 1);
                    return;
                }
            }

            // If the active result is the last on the current page or there are no results on the current page,
            // Find the first result on subsequent pages (PageNumber > currentPage)
            int nextIndex = searchLocations.FindIndex(loc => loc.PageNumber > currentPage);
            if (nextIndex != -1)
            {
                GoToLocation(nextIndex);
            }
        }


        private void SearchLastButton_Click(object sender, EventArgs e)
        {
            if (searchLocations != null && searchLocations.Count > 0)
            {
                GoToLocation(searchLocations.Count - 1);
            }
        }

        private async void PersonalDataButton_Click(object sender, EventArgs e)
        {
            personalDataButton.Enabled = false;
            ClearSearchResult();
            
            groupBoxSearch.Enabled = false;
            searchLocations = await Task.Run(() => PdfTextSearcher.FindTextLocations(inputPdfPath, "", true, userPassword, this));
            groupBoxSearch.Enabled = true;
            searchResultLabel.Text = $"znaleziono: {searchLocations.Count}";
            


            foreach (var itemSl in searchLocations)
            {
                if (itemSl is TextLocation searchLocation)
                {
                    int pageNumber = searchLocation.PageNumber;
                    PageItemStatus status = allPageStatuses[pageNumber - 1];
                    
                    status.HasSearchResults = true;
                    if ((string)filterComboBox.SelectedItem == allComboItem)
                    {
                        ListViewItem item = pagesListView.Items[pageNumber - 1];
                        UpdateItemTag(item, pageNumber, status.HasSelections, status.HasSearchResults, status.MarkedForDeletion, status.HasTextAnnotations);
                        pagesListView.Invalidate(item.Bounds);
                    }
                    else
                    {
                        // rebuild list according to filter
                        ApplyFilter((string)filterComboBox.SelectedItem);
                    }

                }
            }
            personalDataButton.Enabled = true;
            pdfViewer.Invalidate();
            if (searchLocations.Count == 0)
            {
                MessageBox.Show(this, Resources.Msg_NoIdentifiersFound, Resources.Title_Info, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            else
            {
                
                GoToLocation(0);
            }

        }

        private void ClearSearchResult()
        {

            searchLocations.Clear();
            currentLocationIndex = -1;

            foreach (PageItemStatus status in allPageStatuses)
            {
                status.HasSearchResults = false;
            }
            if ((string)filterComboBox.SelectedItem == allComboItem)
            {
                foreach (ListViewItem item in pagesListView.Items)
                {
                    if (item.Tag is PageItemStatus status && status.HasSearchResults)
                    {
                        // Ustawienie flagi na false
                        status.HasSearchResults = false;
                        // Refresh specific item to make the change visible
                        pagesListView.Invalidate(item.Bounds);
                    }
                }
            }
            else
            {
                ApplyFilter((string)filterComboBox.SelectedItem);
            }

        }
 

        private void SearchClearButton_Click(object sender, EventArgs e)
        {
            ClearSearchResult();
            
            searchResultLabel.Text = string.Empty;
            UpdateSearchNavigationButtons();
            pdfViewer.Invalidate();
        }

        private iText.Kernel.Geom.Rectangle ConvertToItTextRectangle(System.Drawing.RectangleF rectF)
        {
            return new iText.Kernel.Geom.Rectangle(rectF.X, rectF.Y, rectF.Width, rectF.Height);
        }

        

        private System.Drawing.RectangleF ConvertToItTextRectangleF(iText.Kernel.Geom.Rectangle rect)
        {
            return new System.Drawing.RectangleF(rect.GetX(), rect.GetY(), rect.GetWidth(), rect.GetHeight());
        }


        private void SearchToSelectionButton_Click(object sender, EventArgs e)
        {
            // Get search results for the current page
            var currentPageSearchResults = searchLocations.Where(loc => loc.PageNumber == currentPage).ToList();
            if (currentPageSearchResults.Count == 0)
            {
                MessageBox.Show(this, Resources.Msg_NoSearchResultsOnPage, Resources.Title_Info, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Convert results - for each result calculate rectangle after width correction
            var convertedResults = currentPageSearchResults.Select(result =>
            {
                int rotation = GetEffectiveRotationDegrees(result.PageNumber);
                                                    // Subtract correction from width to remove excess letter
                var screenRect = new System.Drawing.RectangleF(
                    result.Rect.GetX() + searchWidthCorrection,
                    result.Rect.GetY(),
                    result.Rect.GetWidth() - 2 * searchWidthCorrection,
                    result.Rect.GetHeight());
                // Convert rectangle from iText layout to layout used by redactionBlocks
                var convertedRect = ConvertToPdfCoordinates(screenRect, result.PageNumber, rotation);
                return new { result.PageNumber, ConvertedRect = convertedRect };
            }).ToList();

            // Check whether the current page already has at least one block matching search results
            bool alreadyAdded = redactionBlocks.Any(rb => rb.PageNumber == currentPage &&
                convertedResults.Any(cr => RectEquals(
                    ConvertToItTextRectangle(rb.Bounds),
                    ConvertToItTextRectangle(cr.ConvertedRect), 0.01f)));

            if (alreadyAdded)
            {
                // Remove blocks that correspond to search results
                redactionBlocks.RemoveAll(rb => rb.PageNumber == currentPage &&
                    convertedResults.Any(cr => RectEquals(
                        ConvertToItTextRectangle(rb.Bounds),
                        ConvertToItTextRectangle(cr.ConvertedRect), 0.01f)));
            }
            else
            {
                // Add each search result to redactionBlocks
                foreach (var cr in convertedResults)
                {
                    redactionBlocks.Add(new RedactionBlock(cr.ConvertedRect, cr.PageNumber));
                }
            }

            if (convertedResults.Count>0)
            {
                projectWasChangedAfterLastSave = true;
                saveProjectButton.Enabled = true;
                saveProjectMenuItem.Enabled = true;

            }
            PageItemStatus status = allPageStatuses[currentPage - 1];

            // Update tag of current list item (example UpdateItemTag method)
            status.HasSelections = redactionBlocks.Any(rb => rb.PageNumber == currentPage);
            
            if ((string)filterComboBox.SelectedItem == allComboItem)
            {
                ListViewItem currentItem = pagesListView.Items[currentPage - 1];
                UpdateItemTag(currentItem, currentPage, status.HasSelections, status.HasSearchResults, status.MarkedForDeletion, status.HasTextAnnotations);
                pagesListView.Invalidate(currentItem.Bounds);
            }
            else
            {
                ApplyFilter((string)filterComboBox.SelectedItem);
            }

            UpdateSelectionNavigationButtons();
            renderTimer.Stop();
            renderTimer.Start();
            pdfViewer.Invalidate();
        }

        private static bool RectEquals(iText.Kernel.Geom.Rectangle rect1, iText.Kernel.Geom.Rectangle rect2, float tolerance = 0.01f)
        {
            float x1 = rect1.GetX(); float x2 = rect2.GetX();
            float y1 = rect1.GetY(); float y2 = rect2.GetY();
            float w1 = rect1.GetWidth(); float w2 = rect2.GetWidth();
            float h1 = rect1.GetHeight(); float h2 = rect2.GetHeight();

            return Math.Abs(x1 - x2) < tolerance &&
                   Math.Abs(y1 - y2) < tolerance &&
                   Math.Abs(w1 - w2) < tolerance &&
                   Math.Abs(h1 - h2) < tolerance;
        }

        public Bitmap ExtractBitmapFromRectangle(int pageNumber, RectangleF boxPdfCoords)
        {
            // Render page to bitmap in high resolution
            var page = pdf.Pages[pageNumber - 1];

            float dpi = 300; // or more if OCR should be accurate
            int bmpWidth = (int)(page.Width * dpi / 72f);
            int bmpHeight = (int)(page.Height * dpi / 72f);
            


            using (var bmp = new PDFiumSharp.PDFiumBitmap(bmpWidth, bmpHeight, true))
            {
                bmp.FillRectangle(0, 0, bmpWidth, bmpHeight, 0xFFFFFFFF);
                page.Render(renderTarget: bmp, flags: PDFiumSharp.Enums.RenderingFlags.Annotations);

                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms);
                    ms.Position = 0;
                    var pageBitmap = new Bitmap(DrawingImage.FromStream(ms));
                    ApplyRotationOffset(pageBitmap, GetRotationOffset(pageNumber));

                    // Scale the selection box to bitmap coordinates
                    Rectangle cropRect = new Rectangle(
                        (int)(boxPdfCoords.X * dpi / 72f),
                        (int)(boxPdfCoords.Y * dpi / 72f),
                        (int)(boxPdfCoords.Width * dpi / 72f),
                        (int)(boxPdfCoords.Height * dpi / 72f)
                    );
                    // Correction: clip cropRect to bitmap bounds
                    

                    

                    cropRect.Intersect(new Rectangle(0, 0, pageBitmap.Width, pageBitmap.Height));

                    if (cropRect.Width <= 0 || cropRect.Height <= 0)
                    {
                        MessageBox.Show(this, string.Format(Resources.Err_Crop_OutsideBitmap, cropRect), Resources.Title_Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return null;
                    }


                    return pageBitmap.Clone(cropRect, pageBitmap.PixelFormat);
                }
            }
        }

        public string OcrFromBitmap(Bitmap bitmap)
        {
            string result = string.Empty;
            
            try
            {
                // Convert Bitmap to Pix.Image by saving to a MemoryStream
                using (var ms = new MemoryStream())
                {

                    

                    bitmap.Save(ms, ImageFormat.Png);
                    ms.Position = 0;
                    
                    
                    using (var pixImage = TesseractOCR.Pix.Image.LoadFromMemory(ms))
                    {
                        // Resolve tessdata path relative to the executable
                        string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                        string exeDir = System.IO.Path.GetDirectoryName(exePath);
                        string tessDataPath = System.IO.Path.Combine(exeDir, "tessdata");
                        // Initialize OCR engine
                        using (var engine = new Engine(tessDataPath, TesseractOCR.Enums.Language.Polish, TesseractOCR.Enums.EngineMode.Default))
                        {
                            using (var page = engine.Process(pixImage))
                            {
                                result = page.Text;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Error handling
                MessageBox.Show(this, string.Format(Resources.Err_OCR, ex.Message), Resources.Title_Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return result.Trim();
        }




        public string ExtractTextFromRectangle(string pdfPath, RedactionBlock block)
        {
            var props = new ReaderProperties();
            if (!string.IsNullOrEmpty(userPassword))
            {
                props.SetPassword(System.Text.Encoding.UTF8.GetBytes(userPassword));
            }
            using (var pdfDoc = new iText.Kernel.Pdf.PdfDocument(new PdfReader(pdfPath, props).SetUnethicalReading(Properties.Settings.Default.IgnorePdfRestrictions)))
            {
                var page = pdfDoc.GetPage(block.PageNumber);
                var rotation = GetEffectiveRotationDegrees(block.PageNumber);

                var pdfCoordinates = ConvertToPdfCoordinates(block.Bounds, block.PageNumber, rotation);
                iText.Kernel.Geom.Rectangle rectangle = new iText.Kernel.Geom.Rectangle(
                    (int)Math.Round(pdfCoordinates.X),
                    (int)Math.Round(pdfCoordinates.Y),
                    (int)Math.Round(pdfCoordinates.Width),
                    (int)Math.Round(pdfCoordinates.Height)
                );
                
                
                //var strategy = new FilteredTextEventListener(
                //    new LocationTextExtractionStrategy(), filter
                

                CustomTextExtractionStrategy strategy = new CustomTextExtractionStrategy(rectangle);
                PdfTextExtractor.GetTextFromPage(page, strategy);
                
                return strategy.GetResultantText();
            }
        }


        public void CopyTextsFromAllSelectionsOnCurrentPage()
        {
            // Collect all selections for the current page
            var blocks = redactionBlocks
                .Where(b => b.PageNumber == currentPage)
                .OrderBy(b => b.Bounds.Y)
                .ToList();

            if (blocks.Count == 0)
            {
                MessageBox.Show(this, Resources.Msg_NoSelectionsOnPage, Resources.Title_Info, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<string> allExtractedTexts = new List<string>();

            foreach (var block in blocks)
            {
                // Bounds are already in PDF coordinates
                string text = ExtractTextFromRectangle(inputPdfPath, block);

                // If text is empty, try OCR
                if (string.IsNullOrWhiteSpace(text))
                {
                    using (Bitmap bmp = ExtractBitmapFromRectangle(block.PageNumber, block.Bounds))
                    {
                        if (bmp != null)
                        {
                            text = OcrFromBitmap(bmp);
                        }
                    }
                }

                allExtractedTexts.Add(text.Trim());
            }

            // Concatenate all results with two CRLF characters
            string result = string.Join("\r\n\r\n", allExtractedTexts);

            if (!string.IsNullOrWhiteSpace(result))
            {
                Clipboard.SetText(result);
                MessageBox.Show(this, Resources.Msg_TextsCopiedToClipboard, Resources.Title_Success, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(this, Resources.Msg_NoTextFromSelections, Resources.Title_Info, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }


        private void SearchButton_Click(object sender, EventArgs e)
        {
            SearchText();
        }

        private void TutorialToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShowTutorial();
        }

        private void SplitPdfToolStripMenuItem_Click(object sender, EventArgs e)
        {
            InitSplitPdf();
        }

        private void MergePdfToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MergePdfFiles();
        }

        private void AddTextToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AddEditAnnotation();
        }

        private void DeletePageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RemovePage();
        }

        private void RotatePageMenuItem_Click(object sender, EventArgs e)
        {
            RotateCurrentPageClockwise();
        }

        private void CopyToClipboardToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CopyTextsFromAllSelectionsOnCurrentPage();
        }

        private void ExportGraphicsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ExportAllImagesToSourceFolder(inputPdfPath);
        }
    }

    public class TextAnnotation
    {
        public int PageNumber { get; set; }

        public string AnnotationText { get; set; }

        public Font AnnotationFont { get; set; }

        public System.Drawing.Color AnnotationColor { get; set; }

        public System.Windows.Forms.HorizontalAlignment AnnotationAlignment { get; set; }

        public int AnnotationRotation { get; set; }

        public RectangleF AnnotationBounds { get; set; }

        public bool AnnotationIsLocked { get; set; }

        public TextAnnotation()
        {
            PageNumber = 1;
            AnnotationText = "";
            AnnotationFont = new Font("Arial", 12);
            AnnotationColor = System.Drawing.Color.Black;
            AnnotationAlignment = System.Windows.Forms.HorizontalAlignment.Left; // Default left alignment
            AnnotationRotation = 0;
            AnnotationBounds = new RectangleF(0, 0, 100, 30); // Example rectangular area
            AnnotationIsLocked = false;
        }


        public TextAnnotation(int pageNumber, string text, Font font, System.Drawing.Color color, System.Windows.Forms.HorizontalAlignment alignment, RectangleF bounds, bool isLocked = false)
        {
            PageNumber = pageNumber;
            AnnotationText = text;
            AnnotationFont = font;
            AnnotationColor = color;
            AnnotationAlignment = alignment;
            AnnotationRotation = 0;
            AnnotationBounds = bounds;
            AnnotationIsLocked = isLocked;
        }

        public override string ToString()
        {
            // Optional, facilitates debugging and displaying annotation information.
            return $"{AnnotationText} - {AnnotationFont.FontFamily.Name}, {AnnotationFont.Size} pt, {AnnotationColor.Name}, {AnnotationAlignment}, {AnnotationRotation}, {AnnotationIsLocked}";
        }
    }

    public class EditTextDialog : Form
    {
        private Label lblText;
        private RichTextBox txtText;
        private Button btnFont;
        private Button btnColor;
        private Label lblFontDisplay;
        private GroupBox groupBoxAlignment;
        private RadioButton rbLeft;
        private RadioButton rbCenter;
        private RadioButton rbRight;
        private GroupBox groupBoxRotation;
        private Label lblRotation;
        private NumericUpDown nudRotation;
        private FlowLayoutPanel rotationPresetPanel;
        private GroupBox groupBoxSymbols;
        private FlowLayoutPanel symbolsPanel;
        private Button btnOK;
        private Button btnCancel;

        // Properties that allow reading values set by the user
        public string AnnotationText { get; set; }
        public Font AnnotationFont { get; set; }
        public System.Drawing.Color AnnotationColor { get; set; }
        public System.Windows.Forms.HorizontalAlignment AnnotationAlignment { get; set; }
        public int AnnotationRotation { get; set; }
        public Action ApplyChanges { get; set; }
        private bool suppressAutoApply;

        public EditTextDialog()
        {

            // Set default values if nothing was set previously
            if (AnnotationText == null) AnnotationText = "";
            if (AnnotationFont == null) AnnotationFont = new Font("Arial", 12);
            if (AnnotationColor == System.Drawing.Color.Empty) AnnotationColor = System.Drawing.Color.Black;
            AnnotationRotation = NormalizeAngle(AnnotationRotation);

            InitializeComponents();
        }

        private void InitializeComponents()
        {
            this.Text = Resources.EditText_Title;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.Width = 440;
            this.Height = 500;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            // Label: "Enter text:"
            lblText = new Label
            {
                Text = Resources.EditText_LabelText,
                AutoSize = true,
                Location = new Point(10, 10)
            };

            // TextBox - multiline for entering content
            txtText = new RichTextBox
            {
                Multiline = true,
                ScrollBars = RichTextBoxScrollBars.Both,
                Location = new Point(10, 30),
                Size = new Size(400, 100),
                WordWrap = false
            };
            txtText.TextChanged += TxtText_TextChanged;
            

            // Font picker button
            btnFont = new Button
            {
                Text = Resources.EditText_ButtonFont,
                Location = new Point(10, 140),
                Size = new Size(100, 30)
            };
            btnFont.Click += BtnFont_Click;

            // Label showing the current font selection
            lblFontDisplay = new Label
            {
                Text = $"{AnnotationFont.FontFamily.Name}, {AnnotationFont.Size} pt",
                AutoSize = true,
                Location = new Point(120, 147)
            };

            // Button to choose color
            btnColor = new Button
            {
                Text = Resources.EditText_ButtonColor,
                Location = new Point(300, 140),
                Size = new Size(100, 30)
            };
            btnColor.Click += BtnColor_Click;

            // GroupBox for alignment selection
            groupBoxAlignment = new GroupBox
            {
                Text = Resources.EditText_GroupAlignment,
                Location = new Point(10, 200),
                Size = new Size(400, 50)
            };

            // RadioButton for left alignment
            rbLeft = new RadioButton
            {
                Text = Resources.EditText_AlignLeft,
                Location = new Point(10, 20),
                AutoSize = true,
                Checked = true
            };

            // RadioButton for center alignment
            rbCenter = new RadioButton
            {
                Text = Resources.EditText_AlignCenter,
                Location = new Point(150, 20),
                AutoSize = true
            };

            // RadioButton for right alignment
            rbRight = new RadioButton
            {
                Text = Resources.EditText_AlignRight,
                Location = new Point(290, 20),
                AutoSize = true
            };

            groupBoxAlignment.Controls.Add(rbLeft);
            groupBoxAlignment.Controls.Add(rbCenter);
            groupBoxAlignment.Controls.Add(rbRight);

            rbLeft.CheckedChanged += RadioButton_CheckedChanged;
            rbCenter.CheckedChanged += RadioButton_CheckedChanged;
            rbRight.CheckedChanged += RadioButton_CheckedChanged;

            // GroupBox for rotation selection
            groupBoxRotation = new GroupBox
            {
                Text = Resources.EditText_GroupRotation,
                Location = new Point(10, 260),
                Size = new Size(400, 55)
            };

            lblRotation = new Label
            {
                Text = Resources.EditText_RotationLabel,
                Location = new Point(10, 22),
                AutoSize = true
            };

            nudRotation = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 359,
                Increment = 1,
                Value = NormalizeAngle(AnnotationRotation),
                Location = new Point(90, 18),
                Size = new Size(70, 22)
            };
            nudRotation.ValueChanged += RotationValueChanged;

            rotationPresetPanel = new FlowLayoutPanel
            {
                Location = new Point(170, 18),
                Size = new Size(220, 28),
                AutoSize = false,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0)
            };

            int[] presets = { 0, 30, 45, 90, 180, 270 };
            foreach (int preset in presets)
            {
                Button presetButton = new Button
                {
                    Text = preset.ToString(CultureInfo.InvariantCulture),
                    Tag = preset,
                    Size = new Size(34, 24),
                    Margin = new Padding(2, 0, 0, 0),
                    TabStop = false
                };
                presetButton.Click += RotationPresetButton_Click;
                rotationPresetPanel.Controls.Add(presetButton);
            }

            groupBoxRotation.Controls.Add(lblRotation);
            groupBoxRotation.Controls.Add(nudRotation);
            groupBoxRotation.Controls.Add(rotationPresetPanel);

            // GroupBox for symbol gallery
            groupBoxSymbols = new GroupBox
            {
                Text = Resources.EditText_GroupSymbols,
                Location = new Point(10, 330),
                Size = new Size(400, 65)
            };

            symbolsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(6, 5, 6, 5)
            };

            string[] symbols = { "─", "°", "²", "³", "§", "•", "✓", "✗", "→", "±" };
            foreach (string symbol in symbols)
            {
                Button btnSymbol = new Button
                {
                    Text = symbol,
                    Size = new Size(32, 28),
                    Margin = new Padding(4, 0, 0, 0),
                    TabStop = false
                };
                btnSymbol.Click += SymbolButton_Click;
                symbolsPanel.Controls.Add(btnSymbol);
            }
            groupBoxSymbols.Controls.Add(symbolsPanel);

            // OK and Cancel buttons
            btnOK = new Button
            {
                Text = Resources.Merge_OK,
                Location = new Point(240, 420),
                Size = new Size(80, 30),
                DialogResult = DialogResult.OK
            };
            btnOK.Click += BtnOK_Click;

            btnCancel = new Button
            {
                Text = Resources.Merge_Cancel,
                Location = new Point(330, 420),
                Size = new Size(80, 30),
                DialogResult = DialogResult.Cancel
            };

            // Dodanie kontrolek do formularza
            this.Controls.Add(lblText);
            this.Controls.Add(txtText);
            this.Controls.Add(btnFont);
            this.Controls.Add(lblFontDisplay);
            this.Controls.Add(btnColor);
            this.Controls.Add(groupBoxAlignment);
            this.Controls.Add(groupBoxRotation);
            this.Controls.Add(groupBoxSymbols);
            this.Controls.Add(btnOK);
            this.Controls.Add(btnCancel);

            
            this.CancelButton = btnCancel;
            this.AcceptButton = null;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            suppressAutoApply = true;
            // Load previously set values into form controls
            txtText.Text = AnnotationText;
            // Update the font label
            UpdateFontDisplay();

            // Set alignment - select appropriate radio button
            switch (AnnotationAlignment)
            {
                case System.Windows.Forms.HorizontalAlignment.Left:
                    rbLeft.Checked = true;
                    break;
                case System.Windows.Forms.HorizontalAlignment.Center:
                    rbCenter.Checked = true;
                    break;
                case System.Windows.Forms.HorizontalAlignment.Right:
                    rbRight.Checked = true;
                    break;
            }

            nudRotation.Value = NormalizeAngle(AnnotationRotation);
            suppressAutoApply = false;
        }

        private void UpdateFontDisplay()
        {
            
            string fontStyles = "";
            if (AnnotationFont.Bold)
                fontStyles += "B";
            if (AnnotationFont.Italic)
                fontStyles += "I";
            if (AnnotationFont.Strikeout)
                fontStyles += "S";
            if (AnnotationFont.Underline)
                fontStyles += "U";
            if (!string.IsNullOrEmpty(fontStyles))
                fontStyles = " (" + fontStyles + ")";

            lblFontDisplay.Text = $"{AnnotationFont.FontFamily.Name}, {AnnotationFont.Size} pt {fontStyles}";

            txtText.Font = AnnotationFont;
            txtText.ForeColor = AnnotationColor;
        }

        private void TxtText_TextChanged(object sender, EventArgs e)
        {
            TryApplyChanges();
        }


        private void RadioButton_CheckedChanged(object sender, EventArgs e)
        {
            if (rbLeft.Checked)
            {
                AnnotationAlignment = System.Windows.Forms.HorizontalAlignment.Left; ;
            }
            else if (rbCenter.Checked)
            {
                AnnotationAlignment = System.Windows.Forms.HorizontalAlignment.Center;
            }
            else if (rbRight.Checked)
            {
                AnnotationAlignment = System.Windows.Forms.HorizontalAlignment.Right;
            }
            txtText.SelectAll();
            txtText.SelectionAlignment = AnnotationAlignment;
            txtText.DeselectAll();
            TryApplyChanges();

        }

        private void RotationValueChanged(object sender, EventArgs e)
        {
            AnnotationRotation = NormalizeAngle((int)nudRotation.Value);
            TryApplyChanges();
        }

        private void RotationPresetButton_Click(object sender, EventArgs e)
        {
            if (sender is Button btn)
            {
                int value = 0;
                if (btn.Tag is int tagValue)
                {
                    value = tagValue;
                }
                else
                {
                    int.TryParse(btn.Text, out value);
                }

                if (value < nudRotation.Minimum)
                {
                    value = (int)nudRotation.Minimum;
                }
                else if (value > nudRotation.Maximum)
                {
                    value = (int)nudRotation.Maximum;
                }

                nudRotation.Value = value;
                nudRotation.Focus();
            }
        }

        private void BtnFont_Click(object sender, EventArgs e)
        {
            using (FontDialog fontDialog = new FontDialog())
            {
                fontDialog.Font = AnnotationFont;
                if (fontDialog.ShowDialog(this) == DialogResult.OK)
                {
                    AnnotationFont = fontDialog.Font;
                    UpdateFontDisplay();
                    TryApplyChanges();
                }
            }
        }

        private void BtnColor_Click(object sender, EventArgs e)
        {
            using (ColorDialog colorDialog = new ColorDialog())
            {
                colorDialog.Color = AnnotationColor;
                if (colorDialog.ShowDialog(this) == DialogResult.OK)
                {
                    AnnotationColor = colorDialog.Color;
                    txtText.ForeColor = AnnotationColor;
                    TryApplyChanges();
                }
            }
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(txtText.Text.Trim()))
            {
                MessageBox.Show(this, Resources.EditText_EmptyError, Resources.Title_Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.DialogResult = DialogResult.None;
                return;
            }
            AnnotationText = txtText.Text.Trim();
        }

        private static int NormalizeAngle(int rotation)
        {
            rotation %= 360;
            if (rotation < 0)
                rotation += 360;
            return rotation;
        }

        private void SymbolButton_Click(object sender, EventArgs e)
        {
            if (sender is Button btn)
            {
                txtText.SelectedText = btn.Text;
                txtText.Focus();
            }
        }

        private void TryApplyChanges()
        {
            if (ApplyChanges == null || suppressAutoApply)
                return;
            if (string.IsNullOrWhiteSpace(txtText.Text))
                return;
            AnnotationText = txtText.Text;
            ApplyChanges?.Invoke();
        }
    }



    public class SplitDocumentDialog : Form
    {
        private Label lblFile;
        private TextBox txtFilePath;
        private Button btnBrowse;
        private Label lblPageCount;
        private Label lblPages;
        private TextBox txtPageNumbers;
        private Label lblStep;
        private NumericUpDown nudStep;
        private Button btnOK;
        private Button btnCancel;

        // Properties returning selected data
        public string SelectedFile { get; private set; }
        public List<int> PageNumbers { get; private set; } = new List<int>();
        public int DocumentPageCount { get; private set; }  // Number of pages in the PDF
        public int Step { get; private set; }  // Split step

        public SplitDocumentDialog(int numPages = 0, string defaultFile = "")
        {
            DocumentPageCount = numPages;
            SelectedFile = defaultFile;
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            this.Text = Resources.Split_Title;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Width = 400;
            this.Height = 280;

            // Label for file
            lblFile = new Label
            {
                Text = Resources.Split_FileLabel,
                AutoSize = true,
                Location = new Point(10, 20)
            };

            // Text field with selected path (read-only)
            txtFilePath = new TextBox
            {
                Location = new Point(10, 45),
                Width = 280,
                Text = SelectedFile,
                ReadOnly = true
            };

            // "Browse" button to open OpenFileDialog
            btnBrowse = new Button
            {
                Text = Resources.Split_Browse,
                Location = new Point(300, 43)
            };
            btnBrowse.Click += BtnBrowse_Click;

            // Label to display page count
            lblPageCount = new Label
            {
                Text = string.Format(Resources.Split_PageCountLabel, DocumentPageCount),
                AutoSize = true,
                Location = new Point(10, 75)
            };

            // Label for page numbers to split
            lblPages = new Label
            {
                Text = Resources.Split_PagesLabel,
                AutoSize = true,
                Location = new Point(10, 100)
            };

            // Text field for entering page numbers
            txtPageNumbers = new TextBox
            {
                Location = new Point(10, 125),
                Width = 360
            };

            // Label for the step value
            lblStep = new Label
            {
                Text = Resources.Split_StepLabel,
                AutoSize = true,
                Location = new Point(10, 160)
            };

            nudStep = new NumericUpDown
            {
                Location = new Point(140, 160),
                Minimum = 0,
                Width = 50,
                Value = 0
            };

            // OK button
            btnOK = new Button
            {
                Text = Resources.Merge_OK,
                Location = new Point(210, 200),
                DialogResult = DialogResult.OK
            };
            btnOK.Click += BtnOK_Click;

            // Cancel button
            btnCancel = new Button
            {
                Text = Resources.Merge_Cancel,
                Location = new Point(300, 200),
                DialogResult = DialogResult.Cancel
            };

            // Add controls to the form
            this.Controls.Add(lblFile);
            this.Controls.Add(txtFilePath);
            this.Controls.Add(btnBrowse);
            this.Controls.Add(lblPageCount);
            this.Controls.Add(lblPages);
            this.Controls.Add(txtPageNumbers);
            this.Controls.Add(lblStep);
            this.Controls.Add(nudStep);
            this.Controls.Add(btnOK);
            this.Controls.Add(btnCancel);

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = Resources.Dialog_Filter_PDF;
                openFileDialog.Title = Resources.Split_Dlg_Title;

                if (openFileDialog.ShowDialog(this) == DialogResult.OK)
                {
                    txtFilePath.Text = openFileDialog.FileName;
                    SelectedFile = openFileDialog.FileName;

                    // After selecting PDF file, display number of pages
                    try
                    {
                        var props = new ReaderProperties();
                        //if (!string.IsNullOrEmpty(userPassword))
                        //{
                        
                        //}
                        using (PdfReader reader = new PdfReader(SelectedFile, props).SetUnethicalReading(Properties.Settings.Default.IgnorePdfRestrictions))
                        using (iText.Kernel.Pdf.PdfDocument pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader))
                        {
                            DocumentPageCount = pdfDoc.GetNumberOfPages();
                        }
                        lblPageCount.Text = string.Format(Resources.Split_PageCountLabel, DocumentPageCount);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, string.Format(Resources.Split_Err_ReadFile, ex.Message), Resources.Title_Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        lblPageCount.Text = Resources.Split_PageCountUnknown;
                        DocumentPageCount = 0;
                    }
                }
            }
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            // Validation - whether file was selected and exists
            if (string.IsNullOrEmpty(SelectedFile) || !File.Exists(SelectedFile))
            {
                MessageBox.Show(this, Resources.Split_Err_SelectFile, Resources.Title_Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.DialogResult = DialogResult.None;
                return;
            }

            // Validation - whether page numbers for splitting were entered
            string input = txtPageNumbers.Text;
            var numbers = input.Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            List<int> parsedNumbers = new List<int>();
            foreach (var numStr in numbers)
            {
                if (int.TryParse(numStr.Trim(), out int num) && num > 0)
                {
                    parsedNumbers.Add(num);
                }
                else
                {
                    MessageBox.Show(this, string.Format(Resources.Err_InvalidPageNumberValue, numStr), Resources.Title_Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    this.DialogResult = DialogResult.None;
                    return;
                }
            }
            
            // Ensure page numbers are unique and sorted
            PageNumbers = parsedNumbers.Distinct().OrderBy(n => n).ToList();
            Step = (int)nudStep.Value;
            if (PageNumbers.Count == 0 && Step == 0)
            {
                MessageBox.Show(this, Resources.Delete_Err_NoData, Resources.Title_Warning, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                this.DialogResult = DialogResult.None;
                return;
            } else
            {
                this.Close();
            }
        }
    }



    public partial class MergeFilesForm : Form
    {
        private BindingList<string> pdfFiles = new BindingList<string>();
        private ListBox listBoxFiles;
        private Button buttonAddFiles;
        private Button buttonAddDirectory;
        private Button buttonRemove;
        private Button buttonUp;
        private Button buttonDown;
        private Button buttonClearAll;
        private Button buttonMerge;
        private Button buttonCancel;

        public MergeFilesForm()
        {
            this.FormClosing += MergeFilesForm_FormClosing;
            InitializeComponent();
            listBoxFiles.DataSource = pdfFiles;
        }

        private void InitializeComponent()
        {
            this.Text = Resources.Merge_Title;
            this.Size = new System.Drawing.Size(580, 400);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            

            listBoxFiles = new ListBox
            {
                Location = new System.Drawing.Point(20, 20),
                Size = new System.Drawing.Size(400, 280),
                HorizontalScrollbar = true,
                SelectionMode = SelectionMode.MultiExtended
            };

            buttonAddFiles = new Button { Text = Resources.Merge_AddFiles, Location = new System.Drawing.Point(440, 20), Width = 100 };
            buttonAddDirectory = new Button { Text = Resources.Merge_AddDirectory, Location = new System.Drawing.Point(440, 60), Width = 100 };
            buttonRemove = new Button { Text = Resources.Merge_RemoveSelected, Location = new System.Drawing.Point(440, 100), Width = 100 };
            buttonClearAll = new Button { Text = Resources.Merge_ClearList, Location = new System.Drawing.Point(440, 140), Width = 100 };
            buttonUp = new Button { Text = Resources.Merge_Up, Location = new System.Drawing.Point(440, 180), Width = 100 };
            buttonDown = new Button { Text = Resources.Merge_Down, Location = new System.Drawing.Point(440, 220), Width = 100 };
            buttonMerge = new Button { Text = Resources.Merge_OK, Location = new System.Drawing.Point(300, 320), Width = 100 };
            buttonCancel = new Button { Text = Resources.Merge_Cancel, Location = new System.Drawing.Point(440, 320), Width = 100, DialogResult = DialogResult.Cancel };

            this.Controls.Add(listBoxFiles);
            this.Controls.Add(buttonAddFiles);
            this.Controls.Add(buttonAddDirectory);
            this.Controls.Add(buttonRemove);
            this.Controls.Add(buttonClearAll);
            this.Controls.Add(buttonUp);
            this.Controls.Add(buttonDown);
            this.Controls.Add(buttonMerge);
            this.Controls.Add(buttonCancel);

            buttonAddFiles.Click += ButtonAddFiles_Click;
            buttonAddDirectory.Click += ButtonAddDirectory_Click;
            buttonRemove.Click += ButtonRemove_Click;
            buttonClearAll.Click += ButtonClearAll_Click;
            buttonUp.Click += ButtonUp_Click;
            buttonDown.Click += ButtonDown_Click;
            buttonMerge.Click += ButtonMerge_Click;
            buttonCancel.Click += ButtonCancel_Click;

            this.CancelButton = buttonCancel;
            this.AcceptButton = null;
        }

        private void MergeFilesForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
                this.Owner?.Activate();
            }
        }

        private void ButtonAddFiles_Click(object sender, EventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog()
            {
                Filter = Resources.Dialog_Filter_PDF,
                Multiselect = true
            };
            if (ofd.ShowDialog(this) == DialogResult.OK)
            {
                foreach (var file in ofd.FileNames)
                {
                    if (!pdfFiles.Contains(file))
                        pdfFiles.Add(file);
                }
            }
        }

        private void ButtonAddDirectory_Click(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    var files = Directory.GetFiles(dlg.SelectedPath, "*.pdf");
                    foreach (var file in files)
                    {
                        if (!pdfFiles.Contains(file))
                            pdfFiles.Add(file);
                    }
                }
            }
        }

        private void ButtonRemove_Click(object sender, EventArgs e)
        {
            var selectedItems = listBoxFiles.SelectedItems.Cast<string>().ToList();
            foreach (var item in selectedItems)
            {
                pdfFiles.Remove(item);
            }
        }

        private void ButtonClearAll_Click(object sender, EventArgs e)
        {
            pdfFiles.Clear();
        }

        private void ButtonUp_Click(object sender, EventArgs e)
        {
            if (listBoxFiles.SelectedItems.Count == 0) return;

            var selected = listBoxFiles.SelectedItems.Cast<string>().ToList();
            var indices = selected.Select(item => pdfFiles.IndexOf(item)).Where(i => i > 0).OrderBy(i => i).ToList();

            var moved = new HashSet<string>(selected);
            for (int i = 1; i < pdfFiles.Count; i++)
            {
                if (moved.Contains(pdfFiles[i]) && !moved.Contains(pdfFiles[i - 1]))
                {
                    string temp = pdfFiles[i - 1];
                    pdfFiles[i - 1] = pdfFiles[i];
                    pdfFiles[i] = temp;
                }
            }

            ReselectItems(selected);
        }

        private void ButtonDown_Click(object sender, EventArgs e)
        {
            if (listBoxFiles.SelectedItems.Count == 0) return;
            var selected = listBoxFiles.SelectedItems.Cast<string>().ToList();
            var indices = selected.Select(item => pdfFiles.IndexOf(item)).Where(i => i < pdfFiles.Count - 1).OrderByDescending(i => i).ToList();

            foreach (var i in indices)
            {
                var temp = pdfFiles[i + 1];
                pdfFiles[i + 1] = pdfFiles[i];
                pdfFiles[i] = temp;
            }

            ReselectItems(selected);
        }

        private void ReselectItems(List<string> selected)
        {
            listBoxFiles.ClearSelected(); // pierwszy
            this.BeginInvoke(new System.Action(() =>
            {
                listBoxFiles.ClearSelected(); // second inside UI queue
                foreach (var item in selected)
                {
                    int idx = pdfFiles.IndexOf(item);
                    if (idx >= 0)
                        listBoxFiles.SetSelected(idx, true);
                }
            }));
        }

        private void ButtonCancel_Click(object sender, EventArgs e)
        {
            this.Hide();
            this.Owner?.Activate();
        }

        private void ButtonMerge_Click(object sender, EventArgs e)
        {
            if (pdfFiles.Count == 0)
            {
                MessageBox.Show(this, Resources.Msg_FileListEmpty, Resources.Title_Warning, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }

            // najpierw sprawdzamy wszystkie pliki
            List<string> lockedFiles = new List<string>();
            foreach (var file in pdfFiles)
            {
                try
                {
                    using (var reader = new PdfReader(file))
                    using (var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader))
                    {
                        // try to get number of pages - this can already throw an exception
                        int pages = pdfDoc.GetNumberOfPages();

                        // try to copy one page to empty document in memory
                        using (var ms = new MemoryStream())
                        using (var tempWriter = new iText.Kernel.Pdf.PdfWriter(ms))
                        using (var tempDoc = new iText.Kernel.Pdf.PdfDocument(tempWriter))
                        {
                            // this line will throw an exception if the file has restrictions
                            pdfDoc.CopyPagesTo(1, Math.Min(1, pages), tempDoc);
                        }
                    }
                }
                catch (iText.Kernel.Exceptions.BadPasswordException)
                {
                    lockedFiles.Add(file);
                }
                catch (Exception ex)
                {
                    // if it's another error, also treat it as problem with this PDF
                    lockedFiles.Add(file + " (error: " + ex.Message + ")");
                }
            }

            if (lockedFiles.Count > 0)
            {
                // if any file is locked, don't merge
                string msg = "Cannot merge files, the following files have security settings:\n\n";
                foreach (var f in lockedFiles)
                {
                    msg += "- " + Path.GetFileName(f) + "\n";
                }
                MessageBox.Show(this, msg, Resources.Title_Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // if all files are OK, only then ask for destination
            SaveFileDialog sfd = new SaveFileDialog { Filter = Resources.Dialog_Filter_PDF };
            if (sfd.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    using (var writer = new iText.Kernel.Pdf.PdfWriter(sfd.FileName))
                    using (var mergedDoc = new iText.Kernel.Pdf.PdfDocument(writer))
                    {
                        foreach (var file in pdfFiles)
                        {
                            using (var reader = new PdfReader(file))
                            using (var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader))
                            {
                                pdfDoc.CopyPagesTo(1, pdfDoc.GetNumberOfPages(), mergedDoc);
                            }
                        }

                        PDFForm.ApplyDemoWatermarkIfNeeded(mergedDoc);
                    }

                    MessageBox.Show(this, Resources.Merge_Success, Resources.Title_Success, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    this.Hide();
                    this.Owner?.Activate();

                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = sfd.FileName,
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                    }
                    catch (System.ComponentModel.Win32Exception wex)
                    {
                        MessageBox.Show(this, string.Format(Resources.Err_NoAssociatedPdfApp, wex.Message), Resources.Title_Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, string.Format(Resources.Merge_Err_Merge, ex.Message), Resources.Title_Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

    }

    public class ZoomPanel : Panel
    {
        protected override void WndProc(ref Message m)
        {
            const int WM_MOUSEWHEEL = 0x020A;
            if (m.Msg == WM_MOUSEWHEEL)
            {
                // Get delta value and cursor position within control
                int delta = (short)((long)m.WParam >> 16);
                Point mousePos = this.PointToClient(Cursor.Position);
                MouseEventArgs args = new MouseEventArgs(MouseButtons.None, 0, mousePos.X, mousePos.Y, delta);

                // Find parent form and call method handling event
                PDFForm pdfForm = FindForm() as PDFForm;
                if (pdfForm is PDFForm)
                {
                    pdfForm.Panel2_MouseWheel(args);
                }

                // If CTRL is pressed, "eat" message – don't pass to base.WndProc
                if ((Control.ModifierKeys & Keys.Control) == Keys.Control)
                    return;
            }
            base.WndProc(ref m);
        }

    }

    // Class representing text occurrence location
    public class TextLocation
    {
        public int PageNumber { get; set; }
        public int PageRotation { get; set; }
        public iText.Kernel.Geom.Rectangle Rect { get; set; }

        public TextLocation(int pageNumber, int pageRotation, iText.Kernel.Geom.Rectangle rect)
        {
            PageNumber = pageNumber;
            PageRotation = pageRotation;
            Rect = rect;
        }

        public override string ToString()
        {
            return $"Strona: {PageNumber}, Pozycja: {Rect}";
        }
    }



    public class RedactionBlock
    {
        public System.Drawing.RectangleF Bounds { get; set; }
        public int PageNumber { get; set; }

        public RedactionBlock(System.Drawing.RectangleF bounds, int pageNumber)
        {
            Bounds = bounds;
            PageNumber = pageNumber;
        }
    }

    public class SignatureInfo
    {
        public string FieldName { get; set; }
        public string SignerName { get; set; }
        public string SignerTitle { get; set; }
        public DateTime SignDate { get; set; }
    }

    public class SelectSignaturesDialog : Form
    {
        private readonly ListView listView;
        private readonly Button btnOK;
        private readonly Button btnCancel;

        public List<string> SelectedFieldNames { get; private set; } = new List<string>();

        public SelectSignaturesDialog(List<SignatureInfo> signatures, IEnumerable<string> preselectedFields)
        {
            this.Text = Resources.Signatures_Select_Title;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Width = 520;
            this.Height = 360;

            listView = new ListView
            {
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                GridLines = true,
                Location = new Point(10, 10),
                Size = new Size(480, 260)
            };

            listView.Columns.Add(Resources.Signatures_Select_Column_Name, 180);
            listView.Columns.Add(Resources.Signatures_Select_Column_Title, 140);
            listView.Columns.Add(Resources.Signatures_Select_Column_Date, 140);

            HashSet<string> preselected = null;
            if (preselectedFields != null)
            {
                preselected = new HashSet<string>(preselectedFields, StringComparer.OrdinalIgnoreCase);
            }

            foreach (SignatureInfo sig in signatures)
            {
                string signer = string.IsNullOrWhiteSpace(sig.SignerName) ? "-" : sig.SignerName;
                string title = string.IsNullOrWhiteSpace(sig.SignerTitle) ? "-" : sig.SignerTitle;
                string date = sig.SignDate == DateTime.MinValue ? "-" : sig.SignDate.ToString("g", CultureInfo.CurrentCulture);

                ListViewItem item = new ListViewItem(signer);
                item.SubItems.Add(title);
                item.SubItems.Add(date);
                item.Tag = sig.FieldName ?? string.Empty;
                item.Checked = preselected == null || preselected.Contains(sig.FieldName ?? string.Empty);
                listView.Items.Add(item);
            }

            btnOK = new Button
            {
                Text = Resources.Merge_OK,
                Location = new Point(320, 280),
                Size = new Size(80, 30),
                DialogResult = DialogResult.OK
            };

            btnCancel = new Button
            {
                Text = Resources.Merge_Cancel,
                Location = new Point(410, 280),
                Size = new Size(80, 30),
                DialogResult = DialogResult.Cancel
            };

            this.Controls.Add(listView);
            this.Controls.Add(btnOK);
            this.Controls.Add(btnCancel);

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (this.DialogResult == DialogResult.OK)
            {
                SelectedFieldNames = listView.Items
                    .Cast<ListViewItem>()
                    .Where(item => item.Checked && item.Tag is string)
                    .Select(item => (string)item.Tag)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToList();
            }

            base.OnFormClosing(e);
        }
    }

    public class ProjectData
    {
        public List<RedactionBlock> RedactionBlocks { get; set; }
        public HashSet<int> PagesToRemove { get; set; }
        public List<TextAnnotation> TextAnnotations { get; set; }
        public Dictionary<int, int> PageRotationOffsets { get; set; }
        public int CurrentPage { get; set; }
        public List<string> SignaturesToRemove { get; set; }
        public string SignaturesMode { get; set; }
        public String FilePath { get; set; }
    }

    public class PageItemStatus
    {
        public int PageNumber { get; set; }
        public bool MarkedForDeletion { get; set; }
        public bool HasSearchResults { get; set; }
        public bool HasSelections { get; set; }
        public bool HasTextAnnotations { get; set; }
        public bool HasRotation { get; set; }
    }

    public class DeletePagesDialog : Form
    {
        public int StartPage { get; private set; }
        public int EndPage { get; private set; }
        public int Step { get; private set; }
        public bool ApplyDeletion { get; private set; } // true: apply range, false: cancel selection

        private readonly NumericUpDown nudStart;
        private readonly NumericUpDown nudEnd;
        private readonly NumericUpDown nudStep;
        private readonly RadioButton rbApply;
        private readonly RadioButton rbCancel;
        private readonly Button btnOK;
        private readonly Button btnCancel;

        private readonly ErrorProvider errorProvider;

        public DeletePagesDialog(int numPages)
        {
            this.Text = Resources.Delete_Title;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Width = 300;
            this.Height = 300;

            errorProvider = new ErrorProvider
            {
                BlinkStyle = ErrorBlinkStyle.NeverBlink
            };

            // Labels and NumericUpDown controls to select page range
            Label lblStart = new Label() { Text = Resources.Delete_Label_Start, Left = 20, Top = 20, Width = 120 };
            nudStart = new NumericUpDown() { Left = 150, Top = 20, Width = 50,  Minimum = 1, Maximum = numPages, Value = 1 };

            Label lblEnd = new Label() { Text = Resources.Delete_Label_End, Left = 20, Top = 60, Width = 120 };
            nudEnd = new NumericUpDown() { Left = 150, Top = 60, Width = 50, Minimum = 1, Maximum = numPages, Value = numPages };

            Label lblStep = new Label() { Text = Resources.Delete_Label_Step, Left = 20, Top = 100, Width = 120 };
            nudStep = new NumericUpDown() { Left = 150, Top = 100, Width = 50, Minimum = 0, Maximum = numPages, Value = 1 };

            // Two RadioButtons — one to apply, one to cancel the selection
            rbApply = new RadioButton() { Text = Resources.Delete_Radio_Apply, Left = 20, Top = 140, Width = 200 };
            rbCancel = new RadioButton() { Text = Resources.Delete_Radio_Cancel, Left = 20, Top = 170, Width = 200 };

            // By default set that we want to apply selection
            rbApply.Checked = true;

            // Przyciski OK i Anuluj
            btnOK = new Button() { Text = Resources.Merge_OK, Left = 50, Width = 80, Top = 210, DialogResult = DialogResult.OK };
            btnCancel = new Button() { Text = Resources.Merge_Cancel, Left = 150, Width = 80, Top = 210, DialogResult = DialogResult.Cancel };

            // Add controls to the form
            this.Controls.Add(lblStart);
            this.Controls.Add(nudStart);
            this.Controls.Add(lblEnd);
            this.Controls.Add(nudEnd);
            this.Controls.Add(lblStep);
            this.Controls.Add(nudStep);
            this.Controls.Add(rbApply);
            this.Controls.Add(rbCancel);
            this.Controls.Add(btnOK);
            this.Controls.Add(btnCancel);

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;

            // "Live" validation
            nudStart.ValueChanged += Nud_ValueChanged;
            nudEnd.ValueChanged += Nud_ValueChanged;
            nudStep.ValueChanged += Nud_ValueChanged;

            nudStart.MouseClick += Nud_MouseClick;
            nudEnd.MouseClick += Nud_MouseClick;
            nudStep.MouseClick += Nud_MouseClick;
        }

        private void Nud_MouseClick(object sender, MouseEventArgs e)
        {
            if (sender is NumericUpDown nud)
                nud.Select(0, nud.Text.Length);
        }

        private void Nud_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
            {
                e.Handled = true;
            }
        }


        private void Nud_ValueChanged(object sender, EventArgs e)
        {
            if (nudStart.Value > nudEnd.Value)
            {
                errorProvider.SetError(nudStart, Resources.Delete_Err_StartGreater);
                btnOK.Enabled = false;
            }
            else
            {
                errorProvider.SetError(nudStart, "");
                btnOK.Enabled = true;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (this.DialogResult == DialogResult.OK)
            {
            // Final validation
                if (nudStart.Value > nudEnd.Value)
                {
                    MessageBox.Show(this, Resources.Delete_Err_StartGreater,
                                    Resources.Title_Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    e.Cancel = true;
                    return;
                }

                StartPage = (int)nudStart.Value;
                EndPage = (int)nudEnd.Value;
                Step = (int)nudStep.Value;

                // Set ApplyDeletion depending on selected RadioButton
                // If rbApply is checked, we want to apply selection, otherwise cancel.
                ApplyDeletion = rbApply.Checked;
            }
            base.OnFormClosing(e);
        }
    }

    public class PdfTextSearcher
    {
        private static readonly string PDDServerUrl = "";
        // Cache for processed lines by file path
        private static readonly Dictionary<string, List<CachedLine>> _lineCache = new Dictionary<string, List<CachedLine>>();

        

        // Structure storing line data
        private class CachedLine
        {
            public int PageNumber { get; set; }
            public int PageRotation { get; set; }
            public string Text { get; set; } = "";
            public float YPosition { get; set; }
            public List<CharacterInfo> Characters { get; set; } = new List<CharacterInfo>();
        }

        private class CharacterInfo
        {
            public char Char { get; set; }
            public KernelGeom.Rectangle BoundingBox { get; set; }
        }

        public static event Action<string> OnCacheStatusChanged;

        public static List<TextLocation> FindTextLocations(string pdfPath, string searchText, bool searchPersonalData, string userPassword, IWin32Window owner = null)
        {
            // Check whether lines for this file are already cached
            if (!_lineCache.ContainsKey(pdfPath))
            {
                CacheLines(pdfPath, userPassword);
            }

            // Perform search based on cache
            return SearchInCachedLines(pdfPath, searchText, searchPersonalData, owner);
        }

        private static void CacheLines(string pdfPath, string userPassword)
        {
            var lines = new List<CachedLine>();


            var props = new ReaderProperties();
            if (!string.IsNullOrEmpty(userPassword))
            {
                props.SetPassword(System.Text.Encoding.UTF8.GetBytes(userPassword));
            }

            using (iText.Kernel.Pdf.PdfDocument pdfDoc = new iText.Kernel.Pdf.PdfDocument(new PdfReader(pdfPath, props).SetUnethicalReading(Properties.Settings.Default.IgnorePdfRestrictions)))
            {
                for (int page = 1; page <= pdfDoc.GetNumberOfPages(); page++)
                {
                    var pageObj = pdfDoc.GetPage(page);
                    int rotation = pageObj.GetRotation();
                    var strategy = new LineExtractionStrategy(page, rotation);

                    PdfCanvasProcessor processor = new PdfCanvasProcessor(strategy);
                    processor.ProcessPageContent(pageObj);

                    lines.AddRange(strategy.ExtractedLines);
            OnCacheStatusChanged?.Invoke($"index page: {page}");
                }
            }
            OnCacheStatusChanged?.Invoke($"");
            // Save in cache
            _lineCache[pdfPath] = lines;
        }

        // Funkcja do odczytu text na podstawie line_number
        private static List<TextLocation> SearchInCachedLines(string pdfPath, string searchText, bool searchPersonalData, IWin32Window owner)
        {

            var locations = new List<TextLocation> { };
            var cachedLines = _lineCache[pdfPath];
            string searchTextLower = searchText.ToLowerInvariant();
            
            int cnt = 0;
            foreach (var line in cachedLines)
            {
                
                string textLower = line.Text.ToLowerInvariant();
                OnCacheStatusChanged?.Invoke($"search page: {line.PageNumber}");
                if (searchPersonalData)
                {
                    SearchPersonalData(line, locations);
                }
                else if (textLower.Contains(searchTextLower))
                {
                    // TODO: add per-line search progress notification
                    int startIndex = textLower.IndexOf(searchTextLower, StringComparison.CurrentCultureIgnoreCase);
                    while (startIndex >= 0)
                    {
                        KernelGeom.Rectangle textRect = GetTextFragmentRectangle(line, startIndex, searchText.Length);
                        if (textRect != null)
                        {
                            KernelGeom.Rectangle adjustedRect = AdjustRectangleForRotation(textRect, line.PageRotation);
                            locations.Add(new TextLocation(line.PageNumber, line.PageRotation, adjustedRect));
                        }
                        startIndex = textLower.IndexOf(searchTextLower, startIndex + 1, StringComparison.CurrentCultureIgnoreCase);
                    }
                }
                cnt++;
            }

            if (searchPersonalData && PDDServerUrl!="")
            {
                DialogResult result = ShowMessageBox(
                    owner,
                    Resources.Msg_Confirm_NameSearchSlow,
                    Resources.Title_Warning,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Exclamation,
                    MessageBoxDefaultButton.Button2);

                if (result == DialogResult.No)
                {
                    return locations;
                }

                using (var client = new HttpClient())
                {
                    var groupedByPage = cachedLines
                        .Select((line, index) => new { Line = line, Index = index })
                        .GroupBy(x => x.Line.PageNumber);

                    foreach (var pageGroup in groupedByPage)
                    {
                        var pageLines = pageGroup.ToList();

                        OnCacheStatusChanged?.Invoke($"search page: {pageGroup.Key}");

                        var reqlines = pageLines.Select(x => new
                        {
                            linenumber = x.Index,
                            text = x.Line.Text
                        }).ToList();

                        var requestData = new { reqlines };
                        string jsonRequest = JsonConvert.SerializeObject(requestData);
                        var content = new StringContent(jsonRequest, System.Text.Encoding.UTF8, "application/json");
                        string jsonOut = "{}";

                        try
                        {
                            var response = client.PostAsync(PDDServerUrl, content)
                                .GetAwaiter()
                                .GetResult();
                            response.EnsureSuccessStatusCode();
                            jsonOut = response.Content.ReadAsStringAsync()
                                .GetAwaiter()
                                .GetResult();
                        }
                        catch {

                            ShowMessageBox(owner, Resources.Msg_NameSearchServiceUnavailable, Resources.Title_Warning, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return locations;
                        }

                        JObject obj = JObject.Parse(jsonOut);
                        JArray respLines = obj["resplines"] as JArray;

                        if (respLines == null) continue;

                        foreach (var respLine in respLines)
                        {
                            int lineNumber = int.Parse(respLine["linenumber"]?.ToString() ?? "-1");
                            if (lineNumber < 0 || lineNumber >= cachedLines.Count) continue;

                            var cachedLine = cachedLines[lineNumber];
                            var entities = respLine["entities"] as JArray;
                            if (entities == null || entities.Count == 0) continue;

                            string textLower = cachedLine.Text.ToLowerInvariant();

                            foreach (var entity in entities)
                            {
                                string entityText = entity["text"]?.ToString();
                                if (string.IsNullOrWhiteSpace(entityText)) continue;

                                string entityLower = entityText.ToLowerInvariant();
                                int startIndex = textLower.IndexOf(entityLower, StringComparison.CurrentCultureIgnoreCase);

                                while (startIndex >= 0)
                                {
                                    var textRect = GetTextFragmentRectangle(cachedLine, startIndex, entityText.Length);
                                    if (textRect != null)
                                    {
                                        var adjustedRect = AdjustRectangleForRotation(textRect, cachedLine.PageRotation);
                                        locations.Add(new TextLocation(cachedLine.PageNumber, cachedLine.PageRotation, adjustedRect));
                                    }
                                    startIndex = textLower.IndexOf(entityLower, startIndex + 1, StringComparison.CurrentCultureIgnoreCase);
                                }
                            }
                        }
                    }
                }

                locations = locations
                    .OrderBy(loc => loc.PageNumber)
                    .ThenByDescending(loc => loc.Rect.GetY())
                    .ToList();
            }
            OnCacheStatusChanged?.Invoke("");

            return locations;
        }

        private static DialogResult ShowMessageBox(
            IWin32Window owner,
            string text,
            string caption,
            MessageBoxButtons buttons,
            MessageBoxIcon icon,
            MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
        {
            if (owner is Control control && control.InvokeRequired)
            {
                return (DialogResult)control.Invoke(new Func<DialogResult>(() =>
                    MessageBox.Show(owner, text, caption, buttons, icon, defaultButton)));
            }

            return owner == null
                ? MessageBox.Show(text, caption, buttons, icon, defaultButton)
                : MessageBox.Show(owner, text, caption, buttons, icon, defaultButton);
        }


        // Function to clear cache
        public static void ClearCache(string pdfPath = null)
        {
            if (pdfPath == null)
            {
                // Clear entire cache
                _lineCache.Clear();
            }
            else
            {
                // Clear cache only for specified file
                _lineCache.Remove(pdfPath);
            }
        }

        private class LineExtractionStrategy : LocationTextExtractionStrategy
        {
            private readonly int _pageNum;
            private readonly int _pageRotation;
            public List<CachedLine> ExtractedLines { get; } = new List<CachedLine>();
            private const float Y_TOLERANCE = 2.0f;

            public LineExtractionStrategy(int pageNum, int pageRotation)
            {
                _pageNum = pageNum;
                _pageRotation = pageRotation;
            }

            public override void EventOccurred(IEventData data, EventType type)
            {
                if (type == EventType.RENDER_TEXT && data is TextRenderInfo renderInfo)
                {
                    var baseline = renderInfo.GetBaseline();
                    float yPos = baseline.GetStartPoint().Get(KernelGeom.Vector.I2);

                    CachedLine line = ExtractedLines.Find(l => Math.Abs(l.YPosition - yPos) < Y_TOLERANCE);
                    if (line == null)
                    {
                        line = new CachedLine { PageNumber = _pageNum, PageRotation = _pageRotation, YPosition = yPos };
                        ExtractedLines.Add(line);
                    }

                    string text = renderInfo.GetText();
                    line.Text += text;

                    var charInfos = renderInfo.GetCharacterRenderInfos();
                    if (charInfos != null)
                    {
                        foreach (var charInfo in charInfos)
                        {
                            KernelGeom.LineSegment charBaseline = charInfo.GetBaseline();
                            KernelGeom.LineSegment charAscentLine = charInfo.GetAscentLine();
                            KernelGeom.LineSegment charDescentLine = charInfo.GetDescentLine();

                            KernelGeom.Vector baselineStart = charBaseline.GetStartPoint();
                            KernelGeom.Vector baselineEnd = charBaseline.GetEndPoint();

                            KernelGeom.Vector ascentEnd = charAscentLine.GetEndPoint();
                            KernelGeom.Vector descentStart = charDescentLine.GetStartPoint();

                            float minX = baselineStart.Get(KernelGeom.Vector.I1);
                            float maxX = baselineEnd.Get(KernelGeom.Vector.I1);
                            float minY = baselineStart.Get(KernelGeom.Vector.I2);
                            float maxY = ascentEnd.Get(KernelGeom.Vector.I2) - (minY - descentStart.Get(KernelGeom.Vector.I2)) * 1.5f;

                            line.Characters.Add(new CharacterInfo
                            {
                                Char = charInfo.GetText()[0], // Assume GetText() for single character returns 1 char
                                BoundingBox = new KernelGeom.Rectangle(minX, minY, maxX - minX, maxY - minY)
                            });
                        }
                    }
                }

                base.EventOccurred(data, type);
            }
        }

        private static void SearchPersonalData(CachedLine line, List<TextLocation> locations)
        {
            string text = line.Text;
            Console.WriteLine(text);
            foreach (Match match in PeselPattern.Matches(text))
            {
                if (ValidatePesel(match.Value))
                    AddLocationForMatch(line, match, locations);
            }
            foreach (Match match in PropertyRegisterPattern.Matches(text))
            {
                if (ValidatePropertyRegister(match.Value))
                    AddLocationForMatch(line, match, locations);
            }
            foreach (Match match in IdCardPattern.Matches(text))
            {
                if (ValidateIdCard(match.Value))
                    AddLocationForMatch(line, match, locations);
            }
            foreach (Match match in EmailPattern.Matches(text))
            {
                AddLocationForMatch(line, match, locations);
            }

        }

        private static void AddLocationForMatch(CachedLine line, Match match, List<TextLocation> locations)
        {
            KernelGeom.Rectangle textRect = GetTextFragmentRectangle(line, match.Index, match.Length);
            if (textRect != null)
            {
                KernelGeom.Rectangle adjustedRect = AdjustRectangleForRotation(textRect, line.PageRotation);
                locations.Add(new TextLocation(line.PageNumber, line.PageRotation, adjustedRect));
            }
        }

        private static KernelGeom.Rectangle GetTextFragmentRectangle(CachedLine line, int startIndex, int length)
        {
            if (string.IsNullOrEmpty(line.Text) || startIndex < 0 || startIndex + length > line.Text.Length)
                return null;

            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minY = float.MaxValue;
            float maxY = float.MinValue;

            for (int i = startIndex; i < startIndex + length && i < line.Characters.Count; i++)
            {
                var charInfo = line.Characters[i];
                minX = Math.Min(minX, charInfo.BoundingBox.GetX());
                maxX = Math.Max(maxX, charInfo.BoundingBox.GetX() + charInfo.BoundingBox.GetWidth());
                minY = Math.Min(minY, charInfo.BoundingBox.GetY());
                maxY = Math.Max(maxY, charInfo.BoundingBox.GetY() + charInfo.BoundingBox.GetHeight());
            }

            if (minX == float.MaxValue || maxX == float.MinValue || minY == float.MaxValue || maxY == float.MinValue)
                return null;

            return new KernelGeom.Rectangle(minX, minY, maxX - minX, maxY - minY);
        }

        // Wzorce dla danych osobowych
        private static readonly Regex PeselPattern = new Regex(@"\d{11}");
        private static readonly Regex PropertyRegisterPattern = new Regex(@"([A-Z]{2}\d{1}[A-Z0-9]{1})/\d{8}/\d{1}");
        private static readonly Regex IdCardPattern = new Regex(@"[A-Z]{3}\s?\d{6}");
        private static readonly Regex EmailPattern = new Regex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}");

        private static bool ValidatePesel(string pesel)
        {
            // Check basic conditions
            if (pesel == null || pesel.Length != 11 || !pesel.All(char.IsDigit))
                return false;

            // Weryfikacja cyfry kontrolnej
            int[] weights = { 1, 3, 7, 9, 1, 3, 7, 9, 1, 3 };
            int sum = 0;
            for (int i = 0; i < 10; i++)
            {
                sum += (pesel[i] - '0') * weights[i];
            }
            int checkDigit = (10 - (sum % 10)) % 10;
            if (checkDigit != (pesel[10] - '0'))
                return false;

            // Extract birth date (only after checking control digit)
            if (!int.TryParse(pesel.Substring(0, 2), out int yearDigits) ||
                !int.TryParse(pesel.Substring(2, 2), out int monthDigits) ||
                !int.TryParse(pesel.Substring(4, 2), out int day))
                return false;

            // Determine full year and actual month
            int fullYear;
            int month;
            if (monthDigits >= 1 && monthDigits <= 12) // 1900-1999
            {
                fullYear = 1900 + yearDigits;
                month = monthDigits;
            }
            else if (monthDigits >= 21 && monthDigits <= 32) // 2000-2099
            {
                fullYear = 2000 + yearDigits;
                month = monthDigits - 20;
            }
            else if (monthDigits >= 81 && monthDigits <= 92) // 1800-1899
            {
                fullYear = 1800 + yearDigits;
                month = monthDigits - 80;
            }
            else if (monthDigits >= 41 && monthDigits <= 52) // 2100-2199
            {
                fullYear = 2100 + yearDigits;
                month = monthDigits - 40;
            }
            else if (monthDigits >= 61 && monthDigits <= 72) // 2200-2299
            {
                fullYear = 2200 + yearDigits;
                month = monthDigits - 60;
            }
            else
            {
                return false; // Invalid month range
            }

            // Weryfikacja daty urodzenia
            try
            {
                DateTime date = new DateTime(fullYear, month, day);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false; // Invalid date (e.g. February 31)
            }
        }

        private static bool ValidatePropertyRegister(string number)
        {
            if (string.IsNullOrEmpty(number))
                return false;

            string pattern = @"^([A-Z]{2}\d{1}[A-Z0-9]{1})/\d{8}/\d{1}$";
            var match = Regex.Match(number, pattern, RegexOptions.IgnoreCase);
            if (!match.Success)
                return false;

            string prefix = match.Groups[1].Value.ToUpperInvariant();

            HashSet<string> allowedPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "BB1B", "BB1C", "BB1Z", "BI1B", "BI1P", "BI1S", "BI2P", "BI3P", "BY1B", "BY1I",
            "BY1M", "BY1N", "BY1S", "BY1T", "BY1U", "BY1Z", "BY2T", "CIKW", "CZ1C", "CZ1L",
            "CZ1M", "CZ1Z", "CZ2C", "DIRS", "EL1B", "EL1D", "EL1E", "EL1I", "EL1N", "EL1O",
            "EL2O", "GD1A", "GD1E", "GD1G", "GD1I", "GD1M", "GD1R", "GD1S", "GD1T", "GD1W",
            "GD1Y", "GD2I", "GD2M", "GD2W", "GL1G", "GL1J", "GL1R", "GL1S", "GL1T", "GL1W",
            "GL1X", "GL1Y", "GL1Z", "GW1G", "GW1K", "GW1M", "GW1S", "GW1U", "JG1B", "JG1J",
            "JG1K", "JG1L", "JG1S", "JG1Z", "KA1B", "KA1C", "KA1D", "KA1I", "KA1J", "KA1K",
            "KA1L", "KA1M", "KA1P", "KA1S", "KA1T", "KA1Y", "KI1A", "KI1B", "KI1H", "KI1I",
            "KI1J", "KI1K", "KI1L", "KI1O", "KI1P", "KI1R", "KI1S", "KI1T", "KI1W", "KN1K",
            "KN1N", "KN1S", "KN1T", "KO1B", "KO1D", "KO1E", "KO1I", "KO1K", "KO1L", "KO1W",
            "KO2B", "KR1B", "KR1C", "KR1E", "KR1H", "KR1I", "KR1K", "KR1M", "KR1O", "KR1P",
            "KR1S", "KR1W", "KR1Y", "KR2E", "KR2I", "KR2K", "KR2P", "KR2Y", "KR3I", "KS1B",
            "KS1E", "KS1J", "KS1K", "KS1S", "KS2E", "KZ1A", "KZ1E", "KZ1J", "KZ1O", "KZ1P",
            "KZ1R", "KZ1W", "LD1B", "LD1G", "LD1H", "LD1K", "LD1M", "LD1O", "LD1P", "LD1R",
            "LD1Y", "LE1G", "LE1J", "LE1L", "LE1U", "LE1Z", "LM1G", "LM1L", "LM1W", "LM1Z",
            "LU1A", "LU1B", "LU1C", "LU1I", "LU1K", "LU1O", "LU1P", "LU1R", "LU1S", "LU1U",
            "LU1W", "LU1Y", "NS1G", "NS1L", "NS1M", "NS1S", "NS1T", "NS1Z", "NS2L", "OL1B",
            "OL1C", "OL1E", "OL1G", "OL1K", "OL1L", "OL1M", "OL1N", "OL1O", "OL1P", "OL1S",
            "OL1Y", "OL2G", "OP1B", "OP1G", "OP1K", "OP1L", "OP1N", "OP1O", "OP1P", "OP1S",
            "OP1U", "OS1M", "OS1O", "OS1P", "OS1U", "OS1W", "PL1C", "PL1E", "PL1G", "PL1L",
            "PL1M", "PL1O", "PL1P", "PL1Z", "PL2M", "PO1A", "PO1B", "PO1D", "PO1E", "PO1F",
            "PO1G", "PO1H", "PO1I", "PO1K", "PO1L", "PO1M", "PO1N", "PO1O", "PO1P", "PO1R",
            "PO1S", "PO1T", "PO1Y", "PO1Z", "PO2A", "PO2H", "PO2P", "PO2T", "PR1J", "PR1L",
            "PR1P", "PR1R", "PR2R", "PT1B", "PT1O", "PT1P", "PT1R", "PT1T", "RA1G", "RA1K",
            "RA1L", "RA1P", "RA1R", "RA1S", "RA1Z", "RA2G", "RA2Z", "RZ1A", "RZ1D", "RZ1E",
            "RZ1R", "RZ1S", "RZ1Z", "RZ2Z", "SI1G", "SI1M", "SI1P", "SI1S", "SI1W", "SI2S",
            "SL1B", "SL1C", "SL1L", "SL1M", "SL1S", "SL1Z", "SO1C", "SR1L", "SR1S", "SR1W",
            "SR1Z", "SR2L", "SR2W", "SU1A", "SU1N", "SU1S", "SW1D", "SW1K", "SW1S", "SW1W",
            "SW1Z", "SW2K", "SZ1C", "SZ1G", "SZ1K", "SZ1L", "SZ1M", "SZ1O", "SZ1S", "SZ1T",
            "SZ1W", "SZ1Y", "SZ2S", "SZ2T", "TB1K", "TB1M", "TB1N", "TB1S", "TB1T", "TO1B",
            "TO1C", "TO1G", "TO1T", "TO1U", "TO1W", "TR1B", "TR1D", "TR1O", "TR1T", "TR2T",
            "WA1G", "WA1I", "WA1L", "WA1M", "WA1N", "WA1O", "WA1P", "WA1W", "WA2M", "WA3M",
            "WA4M", "WA5M", "WA6M", "WL1A", "WL1L", "WL1R", "WL1W", "WL1Y", "WR1E", "WR1K",
            "WR1L", "WR1M", "WR1O", "WR1S", "WR1T", "WR1W", "ZA1B", "ZA1H", "ZA1J", "ZA1K",
            "ZA1T", "ZA1Z", "ZG1E", "ZG1G", "ZG1K", "ZG1N", "ZG1R", "ZG1S", "ZG1W", "ZG2K",
            "ZG2S"
        };
            return allowedPrefixes.Contains(prefix);
        }

        private static bool ValidateIdCard(string idCard)
        {
            // Remove space if exists
            idCard = idCard.Replace(" ", "");
            if (idCard.Length != 9) return false;

            int[] weights = { 7, 3, 1, 7, 3, 1, 7, 3 }; // Wagi dla 3 liter i 5 cyfr (bez cyfry kontrolnej)
            int sum = 0;

            // Sprawdzanie liter (pozycje 0-2)
            for (int i = 0; i < 3; i++)
            {
                if (!char.IsUpper(idCard[i])) return false;
                sum += (idCard[i] - 'A' + 10) * weights[i];
            }

            // Pierwsza cyfra (pozycja 3) to cyfra kontrolna
            if (!char.IsDigit(idCard[3])) return false;
            int checkDigit = idCard[3] - '0';

            // Calculate weighted sum for digits (positions 4-8, i.e. 2nd-6th digit)
            for (int i = 4; i < 9; i++)
            {
                if (!char.IsDigit(idCard[i])) return false;
                sum += (idCard[i] - '0') * weights[i - 1]; // i-1, because we skip control digit
            }

            int calculatedCheckDigit = sum % 10;
            return calculatedCheckDigit == checkDigit;
        }

        private static KernelGeom.Rectangle AdjustRectangleForRotation(KernelGeom.Rectangle rect, int rotation)
        {
            if (rotation == 0 || rotation == 360)
                return rect;
            if (rotation == 90)
            {
                float newX = rect.GetY();
                float newY = -rect.GetX() - rect.GetWidth();
                float newWidth = rect.GetHeight();
                float newHeight = rect.GetWidth();
                return new KernelGeom.Rectangle(newX, newY, newWidth, newHeight);
            }
            else if (rotation == 180)
            {
                float newX = -rect.GetX() - rect.GetWidth();
                float newY = -rect.GetY() - rect.GetHeight();
                return new KernelGeom.Rectangle(newX, newY, rect.GetWidth(), rect.GetHeight());
            }
            else if (rotation == 270)
            {
                float newX = -rect.GetY() - rect.GetHeight();
                float newY = rect.GetX();
                float newWidth = rect.GetHeight();
                float newHeight = rect.GetWidth();
                return new KernelGeom.Rectangle(newX, newY, newWidth, newHeight);
            }
            return rect;
        }
    }



    class CustomTextExtractionStrategy : ITextExtractionStrategy
    {
        private readonly iText.Kernel.Geom.Rectangle _targetRect;
        private readonly List<TextChunk> _textChunks;
        private readonly float _yTolerance = 1.0f; // Tolerance for Y coordinate (in points)
        private readonly bool _sortByX = false; // Set to true if you want to sort by X
        private readonly bool _reverseOrder = false; // Ustaw na true dla tekstu od prawej do lewej

        public CustomTextExtractionStrategy(iText.Kernel.Geom.Rectangle targetRect)
        {
            _targetRect = targetRect;
            _textChunks = new List<TextChunk>();
        }

        public void EventOccurred(IEventData data, EventType type)
        {
            if (type.Equals(EventType.RENDER_TEXT))
            {
                TextRenderInfo renderInfo = (TextRenderInfo)data;
                foreach (TextRenderInfo chunk in renderInfo.GetCharacterRenderInfos())
                {
                    // Get ascent and descent lines for each character
                    var ascentLine = chunk.GetAscentLine();
                    var descentLine = chunk.GetDescentLine();

                    // Get character bounding box coordinates
                    float x1 = Math.Min(ascentLine.GetStartPoint().Get(0), descentLine.GetStartPoint().Get(0));
                    float x2 = Math.Max(ascentLine.GetEndPoint().Get(0), descentLine.GetEndPoint().Get(0));
                    float y1 = descentLine.GetStartPoint().Get(1); // Bottom edge (descent)
                    float y2 = ascentLine.GetStartPoint().Get(1); // Top edge (ascent)

                    // Check whether the character's bounding box touches or is inside the rectangle
                    bool intersects = IsBoundingBoxInRectangle(x1, y1, x2, y2, _targetRect);

                    // Add character if its bounding box meets conditions
                    if (intersects)
                    {
                        _textChunks.Add(new TextChunk(chunk.GetText(), y1, x1));
                        // Debug: print coordinates (uncomment if needed)
                    }
                }
            }
        }

        private bool IsBoundingBoxInRectangle(float x1, float y1, float x2, float y2, iText.Kernel.Geom.Rectangle rect)
        {
            float rectLeft = rect.GetLeft();
            float rectRight = rect.GetRight();
            float rectBottom = rect.GetBottom();
            float rectTop = rect.GetTop();

            bool xOverlap = (x1 <= rectRight && x2 >= rectLeft); // At least partial horizontal coverage
            bool yOverlap = (y1 <= rectTop && y2 >= rectBottom); // At least partial vertical coverage

            return xOverlap && yOverlap;
        }

        public string GetResultantText()
        {
            // Group characters by line (based on Y coordinate)
            Dictionary<float, List<TextChunk>> lines = new Dictionary<float, List<TextChunk>>();
            foreach (var chunk in _textChunks)
            {
                float roundedY = (float)Math.Round(chunk.Y / _yTolerance) * _yTolerance;
                if (!lines.ContainsKey(roundedY))
                    lines[roundedY] = new List<TextChunk>();
                lines[roundedY].Add(chunk);
            }

            // Buduj tekst
            System.Text.StringBuilder result = new System.Text.StringBuilder();
            foreach (var line in lines)
            {
                // Sort characters in line by X if sorting is enabled
                if (_sortByX)
                {
                    line.Value.Sort((a, b) => _reverseOrder ? b.X.CompareTo(a.X) : a.X.CompareTo(b.X));
                }
                foreach (var chunk in line.Value)
                {
                    result.Append(chunk.Text);
                }
                result.AppendLine();
            }

            return result.ToString();
        }

        public string GetResultantText(ITextChunkLocation location) => GetResultantText();

        public ICollection<EventType> GetSupportedEvents()
        {
            return new List<EventType> { EventType.RENDER_TEXT };
        }

        private class TextChunk
        {
            public string Text { get; }
            public float Y { get; }
            public float X { get; }

            public TextChunk(string text, float y, float x)
            {
                Text = text;
                Y = y;
                X = x;
            }
        }
    }

    public static class AuditLogger
    {
        /// <summary>
        /// Saves to [dbo].[AnonPDF] login and ip of current user/station.
        /// Columns [id] (IDENTITY) and [datetime] (DEFAULT GETDATE()) are skipped.
        /// </summary>
        public static void LogUsage(string connectionString)
        {
            string login = GetCurrentLogin();
            string ip = GetPreferredIPv4() ?? "0.0.0.0";

            const string sql = @"INSERT INTO dbo.AnonPDF ([login], [ip]) VALUES (@login, @ip);";

            using (var conn = new SqlConnection(connectionString))
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@login", login);
                cmd.Parameters.AddWithValue("@ip", ip);

                conn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private static string GetCurrentLogin()
        {
            // Full domain login if available (DOMAIN\User); fallback: Environment.UserName
            try
            {
                var id = WindowsIdentity.GetCurrent();
                if (id != null && !string.IsNullOrWhiteSpace(id.Name))
                    return id.Name;
            }
            catch { /* ignore; use fallback */ }

            return Environment.UserName ?? "unknown";
        }

        private static string GetPreferredIPv4()
        {
            try
            {
                // 1) Active interfaces (OperationalStatus.Up), exclude loopback/tunnel, IPv4 unicast
                var candidates =
                    NetworkInterface.GetAllNetworkInterfaces()
                        .Where(nic =>
                            nic.OperationalStatus == OperationalStatus.Up &&
                            nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                            nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                        .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
                        .Where(ua => ua?.Address != null && ua.Address.AddressFamily == AddressFamily.InterNetwork)
                        .Select(ua => ua.Address)
                        .Where(addr =>
                            !IPAddress.IsLoopback(addr) &&
                            addr.ToString() != "0.0.0.0" &&
                            !addr.ToString().StartsWith("169.254.")) // unikaj APIPA
                        .Select(addr => addr.ToString())
                        .Distinct()
                        .ToList();

                if (candidates.Count > 0)
                    return candidates.First();

                // 2) Fallback: Dns na hostname
                var host = Dns.GetHostEntry(Dns.GetHostName());
                var ip = host.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                if (ip != null) return ip.ToString();
            }
            catch
            {
                // ignore, use default value above
            }

            return null;
        }
    }

}

#pragma warning restore SPELL
