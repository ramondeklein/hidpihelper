using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Win32;
using PropertyChanged;

namespace HiDpiFixer
{
    public partial class MainWindow : Window
    {
        [ImplementPropertyChanged]
        private class Model
        {
            public string Filename { get; set; }

            public Manifest OriginalManifest { get; private set; }
            public Manifest PatchedManifest { get; private set; }

            public bool? OriginalIsDpiAware { get; private set; }
            public bool? IsDpiAware { get; set; }

            public ICommand BrowseFileCommand { get; }
            public ICommand SaveManifestCommand { get; }

            public Model()
            {
                BrowseFileCommand = new DelegateCommand(OnBrowseFile);
                SaveManifestCommand = new DelegateCommand(OnSaveManifest, OnCanSaveManifest);
            }

            private void OnBrowseFile()
            {
                var ofd = new OpenFileDialog
                {
                    CheckFileExists = true,
                    CheckPathExists = true,
                    Filter = "Executables (*.exe)|*.exe|All Files (*.*)|*.*"
                };
                if (ofd.ShowDialog() == true)
                {
                    Filename = ofd.FileName;

                    // Reload manifests
                    ReloadManifests();
                }
            }

            private bool OnCanSaveManifest()
            {
                return OriginalManifest != null;
            }
        
            private void OnSaveManifest()
            {
                // Update the manifest
                var activeManifest = OriginalManifest ?? PatchedManifest;

                // Patch and save the manifest
                activeManifest.IsDpiAware = IsDpiAware;
                activeManifest.SaveManifest();

                // Reload manifests
                ReloadManifests();
            }

            private void ReloadManifests()
            {
                // Attempt to load the original manifest
                try
                {
                    var originalManifest = new Manifest();
                    originalManifest.LoadFromExe(Filename);
                    OriginalManifest = originalManifest;
                }
                catch (Exception)
                {
                    OriginalManifest = null;
                }

                // Attempt to load the patched manifest
                try
                {
                    var patchedManifest = new Manifest();
                    patchedManifest.LoadFromManifest(Filename + ".manifest");
                    PatchedManifest = patchedManifest;
                }
                catch (Exception)
                {
                    PatchedManifest = null;
                }

                // Set flags whether or not the application is DPI aware
                OriginalIsDpiAware = OriginalManifest?.IsDpiAware;
                IsDpiAware = (PatchedManifest ?? OriginalManifest)?.IsDpiAware;
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            DataContext = new Model();
        }
    }
}
