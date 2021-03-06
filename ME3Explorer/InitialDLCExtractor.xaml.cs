﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
using KFreonLib.Debugging;
using KFreonLib.MEDirectories;
using UsefulThings.WPF;

namespace ME3Explorer
{
    /// <summary>
    /// Interaction logic for InitialDLCExtractor.xaml
    /// </summary>
    public partial class InitialDLCExtractor : Window
    {
        public ViewModel vm = null;

        public InitialDLCExtractor()
        {
            InitializeComponent();
            vm = new ViewModel();
            if (vm.Required == null)
            {
                MessageBox.Show("ME3 not found :(  Check your path and try again.");
                this.Close();
            }
            DataContext = vm;
        }

        private void DontExtractButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            this.Close();
        }

        private async void ExtractButton_Click(object sender, RoutedEventArgs e)
        {
            vm.NotWorking = false;
            bool dlcProblem = false;
            await Task.Run(() =>
            {
                SFAREditor2 dlcedit2 = new SFAREditor2();
                DebugOutput.PrintLn("Extracting DLC...");
                dlcProblem = !dlcedit2.ExtractAllDLC();
                    
            });
            vm.NotWorking = true;
            MessageBox.Show(dlcProblem ? "Some DLC wasn't extracted. This is likely due to pathing. Check the Debug Window." : "DLC Extracted!");
            this.DialogResult = true;
            this.Close();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = null;
            this.Close();
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            vm.ChangePaths();
        }
    }

    public class ViewModel : ViewModelBase
    {
        public string ME1Path
        {
            get
            {
                return ME1Directory.GamePath();
            }
        }

        public string ME2Path
        {
            get
            {
                return ME2Directory.GamePath();
            }
        }

        public string ME3Path
        {
            get
            {
                return ME3Directory.GamePath();
            }
        }




        bool notWorking = true;
        public bool NotWorking
        {
            get
            {
                return notWorking;
            }
            set
            {
                SetProperty(ref notWorking, value);
            }
        }

        public bool? Required = true;

        string requiredSpace = null;
        public string RequiredSpace
        {
            get
            {
                return requiredSpace;
            }
            set
            {
                SetProperty(ref requiredSpace, value);
            }
        }

        string availableSpace = null;
        public string AvailableSpace
        {
            get
            {
                return availableSpace;
            }
            set
            {
                SetProperty(ref availableSpace, value);
            }
        }

        public bool SpaceOK
        {
            get; set;
        }

        public ViewModel()
        {
            if (!Directory.Exists(ME3Directory.GamePath()))
            {
                Required = null;
                return;
            }

            double required = GetRequiredSize();
            if (required == 0)
            {
                Required = false;
                return;
            }

            double available = GetAvailableSpace();

            SpaceOK = available > required;

            RequiredSpace = UsefulThings.General.GetFileSizeAsString(required);
            AvailableSpace = UsefulThings.General.GetFileSizeAsString(available);
        }

        public double GetRequiredSize()
        {
            var folders = Directory.EnumerateDirectories(ME3Directory.DLCPath);
            var extracted = folders.Where(folder => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any(file => file.EndsWith("pcconsoletoc.bin", StringComparison.OrdinalIgnoreCase)));
            var unextracted = folders.Except(extracted);

            double size = 0;
            foreach (var folder in unextracted)
            {
                if (folder.Contains("__metadata"))
                    continue;

                try
                {
                    FileInfo info = new FileInfo(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(file => file.EndsWith(".sfar", StringComparison.OrdinalIgnoreCase)).First());
                    size += info.Length * 1.1; // KFreon: Fudge factor for decompression
                }
                catch(Exception e)
                {
                    DebugOutput.PrintLn(e.Message);
                }
            }
            return size;
        }

        public double GetAvailableSpace()
        {
            var parts = ME3Directory.GamePath().Split(':');
            DriveInfo info = new DriveInfo(parts[0]);
            return info.AvailableFreeSpace;
        }

        public void ChangePaths()
        {
            string[] biogames = new string[3];
            biogames[0] = ME1Directory.GamePath();
            biogames[1] = ME2Directory.GamePath();
            biogames[2] = ME3Directory.GamePath();


            // KFreon: Display PathChanger
            using (KFreonLib.Helpers.PathChanger changer = new KFreonLib.Helpers.PathChanger(biogames[0], biogames[1], biogames[2]))
            {
                if (changer.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    // KFreon: Change paths
                    Properties.Settings.Default.ME1InstallDir = changer.PathME1;
                    Properties.Settings.Default.ME2InstallDir = changer.PathME2;
                    Properties.Settings.Default.ME3InstallDir = changer.PathME3;

                    KFreonLib.MEDirectories.MEDirectories.SaveSettings(new List<string>() { changer.PathME1, changer.PathME2, changer.PathME2 });
                    ME1Directory.GamePath(changer.PathME1);
                    ME2Directory.GamePath(changer.PathME2);
                    ME3Directory.GamePath(changer.PathME3);

                    OnPropertyChanged(nameof(ME1Path));
                    OnPropertyChanged(nameof(ME2Path));
                    OnPropertyChanged(nameof(ME3Path));
                }
            }
        }
    }
}
