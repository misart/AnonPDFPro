# AnonPDF

AnonPDF is a Windows (WinForms) application for PDF anonymization. It lets you redact document areas, remove pages, add text annotations, and search for content to mark quickly. For image-only regions with no text, it can fall back to OCR (Tesseract) for searching.

## Features
- Rectangular area selection and redaction.
- Remove single pages or page ranges.
- Text annotations with configurable font and position.
- Text search and conversion of results into selections.
- Display and report of digital signature status.

## Requirements & Build
- Visual Studio 2019+ (Windows); target .NET Framework as defined by the project.

**First time setup:**
1. Clone the repository
2. Run `restore-packages.bat` (recommended) OR `nuget restore AnonPDF.sln -PackagesDirectory packages`
   - This ensures packages are restored to the local `packages/` folder
   - Required for native PDFium DLLs to be copied correctly during build
3. Open `AnonPDF.sln` in Visual Studio
4. Build and run (F5)

**Alternative - Command line build:**
- Restore: `nuget restore AnonPDF.sln -PackagesDirectory packages`
- Build: `msbuild AnonPDF.sln /p:Configuration=Release`
- Run: `bin\Release\AnonPDF.exe`

**Note:** The `NuGet.Config` file ensures packages are restored locally to `packages/` folder, which is required for native DLLs (pdfium_x64.dll, Tesseract data files) to be copied to the build output.

The help file `UserGuide_*.pdf` is copied to the output (bin) directory during build.

## Debugging in Visual Studio (iText `FontCache` NullReferenceException)

When debugging AnonPDF in Visual Studio, you may see the debugger break with an exception similar to:

> System.NullReferenceException  
> Source = itext.io  
> at iText.IO.Font.FontCache..cctor()

This typically happens when working with signed PDFs (e.g. when calling `SignatureUtil`) and is caused by internal initialization logic in iText.  
The exception is **handled internally by iText** and does **not** indicate a real error in AnonPDF, but Visual Studio may still break on it by default.

If this keeps interrupting your debugging session, you can safely adjust your debugger settings:

1. In Visual Studio, go to  
   **Tools -> Options -> Debugging -> General**
2. Make sure:
   - **Enable Just My Code** is **checked**.
   - You do **not** have a global setting that breaks on **all** thrown CLR exceptions.
3. Optionally, open the **Exception Settings** window and uncheck  
   **Break when thrown** for **Common Language Runtime Exceptions**.

After these changes, the internal iText `FontCache` exception will no longer stop the debugger, while AnonPDF will continue to run normally.

## Usage
- Open a PDF and navigate pages.
- Draw redaction areas, add annotations, remove pages.
- Save the new PDF and optionally open it after saving.

## License & Components
- License: AGPL-3.0-or-later (see `LICENSE`).
- Third-party components: iText 7 (AGPL-3.0), PDFium/PDFiumSharp, Newtonsoft.Json (MIT), BouncyCastle (MIT), TesseractOCR (.NET) (Apache-2.0). Details in `THIRD-PARTY-NOTICES.md`.

## Help & Support
- Help file: use the `Help` menu in the app (opens `UserGuide_*.pdf`).
- Issues and contributions: open issues/PRs on GitHub once the repo is published.

## Disclaimer

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

USE AT YOUR OWN RISK.
