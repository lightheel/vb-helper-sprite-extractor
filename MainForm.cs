using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AssetStudio;

namespace VbHelperSpriteExtractor
{
    public partial class MainForm : Form
    {
        private Button btnSelectAPK;
        private Button btnExtract;
        private TextBox txtAPKPath;
        private TextBox txtOutputPath;
        private ProgressBar progressBar;
        private Label lblStatus;
        private Label lblAPK;
        private Label lblOutput;
        private RichTextBox txtLog;
        private FolderBrowserDialog folderBrowserDialog;
        private OpenFileDialog openFileDialog;

        private AssetsManager assetsManager;
        private Extractor extractor;
        private CancellationTokenSource cancellationTokenSource;
        private Task extractionTask;

        public MainForm()
        {
            InitializeComponent();
            assetsManager = new AssetsManager();
            extractor = new Extractor(assetsManager);
            extractor.ProgressUpdated += OnProgressUpdated;
            extractor.StatusUpdated += OnStatusUpdated;
            extractor.LogMessage += OnLogMessage;
            
            // Handle form closing to ensure proper cleanup
            this.FormClosing += MainForm_FormClosing;
        }

        private void InitializeComponent()
        {
            this.Text = "Vital Bracelet Arena - Dim Sprites Extractor";
            this.Size = new System.Drawing.Size(800, 600);
            this.StartPosition = FormStartPosition.CenterScreen;

            // APK Selection
            lblAPK = new Label
            {
                Text = "APK File:",
                Location = new System.Drawing.Point(12, 15),
                Size = new System.Drawing.Size(80, 23)
            };
            this.Controls.Add(lblAPK);

            txtAPKPath = new TextBox
            {
                Location = new System.Drawing.Point(98, 12),
                Size = new System.Drawing.Size(550, 23),
                ReadOnly = true
            };
            this.Controls.Add(txtAPKPath);

            btnSelectAPK = new Button
            {
                Text = "Browse...",
                Location = new System.Drawing.Point(658, 10),
                Size = new System.Drawing.Size(100, 25)
            };
            btnSelectAPK.Click += BtnSelectAPK_Click;
            this.Controls.Add(btnSelectAPK);

            // Output Path
            lblOutput = new Label
            {
                Text = "Output:",
                Location = new System.Drawing.Point(12, 50),
                Size = new System.Drawing.Size(80, 23)
            };
            this.Controls.Add(lblOutput);

            txtOutputPath = new TextBox
            {
                Location = new System.Drawing.Point(98, 47),
                Size = new System.Drawing.Size(550, 23),
                Text = Path.Combine(Environment.CurrentDirectory, "extracted")
            };
            this.Controls.Add(txtOutputPath);

            Button btnSelectOutput = new Button
            {
                Text = "Browse...",
                Location = new System.Drawing.Point(658, 45),
                Size = new System.Drawing.Size(100, 25)
            };
            btnSelectOutput.Click += BtnSelectOutput_Click;
            this.Controls.Add(btnSelectOutput);

            // Extract Button
            btnExtract = new Button
            {
                Text = "Extract Dim Sprites",
                Location = new System.Drawing.Point(12, 85),
                Size = new System.Drawing.Size(150, 35),
                Enabled = false
            };
            btnExtract.Click += BtnExtract_Click;
            this.Controls.Add(btnExtract);

            // Progress Bar
            progressBar = new ProgressBar
            {
                Location = new System.Drawing.Point(12, 130),
                Size = new System.Drawing.Size(746, 23),
                Style = ProgressBarStyle.Continuous
            };
            this.Controls.Add(progressBar);

            // Status Label
            lblStatus = new Label
            {
                Text = "Ready",
                Location = new System.Drawing.Point(12, 160),
                Size = new System.Drawing.Size(746, 23)
            };
            this.Controls.Add(lblStatus);

            // Log TextBox
            txtLog = new RichTextBox
            {
                Location = new System.Drawing.Point(12, 190),
                Size = new System.Drawing.Size(746, 350),
                ReadOnly = true,
                Font = new System.Drawing.Font("Consolas", 9)
            };
            this.Controls.Add(txtLog);

            // Dialogs
            openFileDialog = new OpenFileDialog
            {
                Filter = "APK Files (*.apk)|*.apk|All Files (*.*)|*.*",
                Title = "Select APK File"
            };

            folderBrowserDialog = new FolderBrowserDialog
            {
                Description = "Select Output Folder"
            };
        }

        private void BtnSelectAPK_Click(object sender, EventArgs e)
        {
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                txtAPKPath.Text = openFileDialog.FileName;
                btnExtract.Enabled = !string.IsNullOrEmpty(txtAPKPath.Text) && 
                                   !string.IsNullOrEmpty(txtOutputPath.Text);
            }
        }

        private void BtnSelectOutput_Click(object sender, EventArgs e)
        {
            if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
            {
                txtOutputPath.Text = folderBrowserDialog.SelectedPath;
                btnExtract.Enabled = !string.IsNullOrEmpty(txtAPKPath.Text) && 
                                   !string.IsNullOrEmpty(txtOutputPath.Text);
            }
        }

        private async void BtnExtract_Click(object sender, EventArgs e)
        {
            btnExtract.Enabled = false;
            btnSelectAPK.Enabled = false;
            progressBar.Value = 0;
            txtLog.Clear();

            // Create cancellation token source for this extraction
            cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = cancellationTokenSource.Token;

            try
            {
                string apkPath = txtAPKPath.Text;
                string outputPath = txtOutputPath.Text;

                if (!File.Exists(apkPath))
                {
                    MessageBox.Show("APK file not found!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (!Directory.Exists(outputPath))
                {
                    Directory.CreateDirectory(outputPath);
                }

                // Store the task so we can wait for it during cleanup
                extractionTask = Task.Run(() => extractor.ExtractFromAPK(apkPath, outputPath), cancellationToken);
                await extractionTask;

                if (!cancellationToken.IsCancellationRequested)
                {
                    MessageBox.Show("Extraction completed successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (OperationCanceledException)
            {
                // Extraction was cancelled - this is expected if window is closed
                OnLogMessage("Extraction cancelled.");
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    MessageBox.Show($"Error during extraction: {ex.Message}\n\n{ex.StackTrace}", 
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                cancellationTokenSource?.Dispose();
                cancellationTokenSource = null;
                extractionTask = null;
                
                if (!this.IsDisposed && !this.Disposing)
                {
                    btnExtract.Enabled = true;
                    btnSelectAPK.Enabled = true;
                }
            }
        }

        private void OnProgressUpdated(int current, int total)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<int, int>(OnProgressUpdated), current, total);
                return;
            }

            if (total > 0)
            {
                progressBar.Maximum = total;
                progressBar.Value = current;
            }
        }

        private void OnStatusUpdated(string status)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(OnStatusUpdated), status);
                return;
            }

            lblStatus.Text = status;
        }

        private void OnLogMessage(string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(OnLogMessage), message);
                return;
            }

            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
            txtLog.ScrollToCaret();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Cancel any ongoing extraction
            if (cancellationTokenSource != null && !cancellationTokenSource.IsCancellationRequested)
            {
                cancellationTokenSource.Cancel();
                
                // Wait a bit for the task to respond to cancellation
                if (extractionTask != null && !extractionTask.IsCompleted)
                {
                    try
                    {
                        extractionTask.Wait(TimeSpan.FromSeconds(2));
                    }
                    catch (AggregateException)
                    {
                        // Expected when task is cancelled
                    }
                }
            }

            // Clean up extractor
            if (extractor != null)
            {
                extractor.ProgressUpdated -= OnProgressUpdated;
                extractor.StatusUpdated -= OnStatusUpdated;
                extractor.LogMessage -= OnLogMessage;
                extractor = null;
            }

            // Dispose resources
            cancellationTokenSource?.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                cancellationTokenSource?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

