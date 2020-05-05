using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LiveCharts;
using LiveCharts.Wpf;
using nihilus.Annotations;
using nihilus.Logic.BackgroundWorker.Performance;
using nihilus.Logic.CustomConsole;
using nihilus.Logic.Manager;
using nihilus.Logic.Model;
using nihilus.Logic.Persistence;
using nihilus.View.Xaml.MainWindowFrames;
using nihilus.Logic;
using nihilus.Logic.RoleManagement;
using nihilus.View.Xaml.Pages;
using nihilus.View.Xaml2.Pages;
using Console = System.Console;
using Timer = System.Timers.Timer;

namespace nihilus.ViewModel
{
    public class ServerViewModel : INotifyPropertyChanged
    {
        private string externalIP = new WebClient().DownloadString("http://icanhazip.com").Trim();

        private CPUTracker cpuTracker;
        private double cpuValue;
        private MEMTracker memTracker;
        private double memValue;

        private Timer restartTimer = null;

        private RoleUpdater whitelistUpdater;
        private RoleUpdater banlistUpdater;
        private RoleUpdater oplistUpdater;

        public ConsoleReader ConsoleReader;
        public ObservableCollection<string> ConsoleOutList { get; }
        public ObservableCollection<Player> PlayerList { get; set; } = new ObservableCollection<Player>();
        public ObservableCollection<Player> BanList { get; set; } = new ObservableCollection<Player>();
        public ObservableCollection<Player> OPList { get; set; } = new ObservableCollection<Player>();
        public ObservableCollection<Player> WhiteList { get; set; } = new ObservableCollection<Player>();
        public ObservableCollection<ServerVersion> Versions { get; set; } = new ObservableCollection<ServerVersion>();
        public string ConsoleIn { get; set; } = "";
        public ServerStatus CurrentStatus { get; set; }
        public Server Server { get; set; }

        public bool RestartEnabled { get; set; }
        public string NextRestartHours { get; set; }
        public string NextRestartMinutes { get; set; }
        public string NextRestartSeconds { get; set; }

        public string ServerTitle => Server.Name + " - " + Server.Version.Type + " " + Server.Version.Version;

        public ImageSource Icon
        {
            get
            {
                BitmapImage bi3 = new BitmapImage();
                bi3.BeginInit();
                switch (Server.Version.Type)
                {
                    case ServerVersion.VersionType.Vanilla:
                        bi3.UriSource = new Uri("pack://application:,,,/View/Resources/images/Icons/Vanilla.png");
                        break;
                    case ServerVersion.VersionType.Paper:
                        bi3.UriSource = new Uri("pack://application:,,,/View/Resources/images/Icons/Paper.png");
                        break;
                    case ServerVersion.VersionType.Spigot:
                        bi3.UriSource = new Uri("pack://application:,,,/View/Resources/images/Icons/Spigot.png");
                        break;
                    default:
                        return null;
                }
                bi3.EndInit();
                return bi3;
            }
        }
        public ImageSource IconW
        {
            get
            {
                BitmapImage bi3 = new BitmapImage();
                bi3.BeginInit();
                switch (Server.Version.Type)
                {
                    case ServerVersion.VersionType.Vanilla:
                        bi3.UriSource = new Uri("pack://application:,,,/View/Resources/images/Icons/VanillaW.png");
                        break;
                    case ServerVersion.VersionType.Paper:
                        bi3.UriSource = new Uri("pack://application:,,,/View/Resources/images/Icons/PaperW.png");
                        break;
                    case ServerVersion.VersionType.Spigot:
                        bi3.UriSource = new Uri("pack://application:,,,/View/Resources/images/Icons/SpigotW.png");
                        break;
                    default:
                        return null;
                }
                bi3.EndInit();
                return bi3;
            }
        }
        

        public Brush IconColor
        {
            get
            {
                switch (CurrentStatus)
                {
                    case ServerStatus.RUNNING: return (Brush)new BrushConverter().ConvertFromString("#5EED80");
                    case ServerStatus.STOPPED: return (Brush)new BrushConverter().ConvertFromString("#565B7A");
                    case ServerStatus.STARTING: return (Brush)new BrushConverter().ConvertFromString("#EBED78");
                    default: return Brushes.White;
                }
            }
        }

        public bool ServerRunning => CurrentStatus == ServerStatus.RUNNING;

        public string AddressInfo { get; set; }

        public string CPUValue => Math.Round(cpuValue,0) + "%";
        public double CPUValueRaw => cpuValue;

        public string MemValue => Math.Round(memValue/Server.JavaSettings.MaxRam *100,0) + "%";
        public double MemValueRaw => memValue/Server.JavaSettings.MaxRam *100;
        public Page ServerPage { get; set; }
        public Page ConsolePage { get; set; }
        public Page SettingsPage { get; set; }

        private ICommand readConsoleIn;

        public ICommand ReadConsoleIn
        {
            get
            {
                return readConsoleIn
                       ?? (readConsoleIn = new ActionCommand(() =>
                       {
                           ConsoleReader?.Read(ConsoleIn);
                           ConsoleIn = "";
                       }));
            }
        }

        public double DownloadProgress { get; set; }
        public string DownloadProgressReadable { get; set; }
        public bool DownloadCompleted { get; 
            set; }

        public ServerViewModel(Server server)
        {
            DateTime start = DateTime.Now;
            Console.WriteLine("Starting initialization of ViewModel for Server "+server.Name);
            Server = server;
            CurrentStatus = ServerStatus.STOPPED;
            ConsoleOutList = new ObservableCollection<string>();
            new Thread(() =>
            {
                if (Server.Version.Type == ServerVersion.VersionType.Vanilla)
                {
                    Versions = VersionManager.Instance.VanillaVersions;
                }
                else if (Server.Version.Type == ServerVersion.VersionType.Paper)
                {
                    Versions = VersionManager.Instance.PaperVersions;
                }
                else if (Server.Version.Type == ServerVersion.VersionType.Spigot)
                {
                    Versions = VersionManager.Instance.SpigotVersions;
                } 
            }).Start();
            
            ConsoleOutList.CollectionChanged += ConsoleOutChanged;
            UpdateAddressInfo();
            Application.Current.Dispatcher.Invoke(new Action(() => ServerPage = new View.Xaml2.Pages.ServerPage(this)));
            Application.Current.Dispatcher.Invoke(new Action(() => ConsolePage = new ConsolePage(this)));
            //Application.Current.Dispatcher.Invoke(new Action(() => SettingsPage = new View.Xaml2.Pages.SettingsPage()));

            WhiteList.CollectionChanged += WhiteListChanged;
            BanList.CollectionChanged += BanListChanged;
            OPList.CollectionChanged += OPListChanged;
            new Thread(() =>
            {
                RoleUpdater.InitializeList(RoleType.WHITELIST, WhiteList, Server);
                RoleUpdater.InitializeList(RoleType.BAN_LIST, BanList, Server);
                RoleUpdater.InitializeList(RoleType.OP_LIST, OPList, Server);
                Console.WriteLine("Finished reading Role-lists");

                whitelistUpdater = new RoleUpdater(RoleType.WHITELIST, WhiteList,Server.Version);
                banlistUpdater = new RoleUpdater(RoleType.BAN_LIST, BanList,Server.Version);
                oplistUpdater = new RoleUpdater(RoleType.OP_LIST, OPList,Server.Version);
            }).Start();

            TimeSpan t = DateTime.Now - start;
            Console.WriteLine("Server ViewModel for " + server + " initialized in "+t.Seconds+"."+t.Milliseconds+"s");
        }

        private void UpdateAddressInfo()
        {
            AddressInfo = externalIP + ":" + Server.ServerSettings.ServerPort;
        }

        public void RoleInputHandler(string line)
        {
            new Thread(() =>
            {
                whitelistUpdater.HandleOutputLine(line);
                banlistUpdater.HandleOutputLine(line);
                oplistUpdater.HandleOutputLine(line);
            }).Start();
        }

        public void SetRestartTime(double time)
        {
            if (time < 0)
            {
                restartTimer?.Dispose();
                RestartEnabled = false;
                return;
            }

            TimeSpan timeSpan = TimeSpan.FromMilliseconds(time);
            new Thread(() =>
            {
                restartTimer = new System.Timers.Timer();
                restartTimer.Interval = 1000;
                restartTimer.Elapsed += (sender, args) =>
                {
                    timeSpan = timeSpan.Subtract(TimeSpan.FromMilliseconds(1000));
                    RestartEnabled = true;
                    NextRestartHours = timeSpan.Hours.ToString();
                    NextRestartMinutes = timeSpan.Minutes.ToString();
                    NextRestartSeconds = timeSpan.Seconds.ToString();
                    if (timeSpan.Hours == 0 && timeSpan.Minutes == 30 && timeSpan.Seconds == 0)
                    {
                        ApplicationManager.Instance.ActiveServers[Server].StandardInput
                            .WriteLineAsync("/say Next server restart in 30 minutes!");
                    }
                    else if (timeSpan.Hours == 0 && timeSpan.Minutes == 5 && timeSpan.Seconds == 0)
                    {
                        ApplicationManager.Instance.ActiveServers[Server].StandardInput
                            .WriteLineAsync("/say Next server restart in 5 minutes!");
                    }
                    else if (timeSpan.Hours == 0 && timeSpan.Minutes == 1 && timeSpan.Seconds == 0)
                    {
                        ApplicationManager.Instance.ActiveServers[Server].StandardInput
                            .WriteLineAsync("/say Next server restart in 1 minute!");
                    }
                };
                restartTimer.AutoReset = true;
                restartTimer.Enabled = true;
            }).Start();
        }

        public void UpdateSettings()
        {
            UpdateAddressInfo();
            new Thread(() =>
            {
                new FileWriter().WriteServerSettings(Path.Combine(App.ApplicationPath,Server.Name), Server.ServerSettings.SettingsDictionary);
                Serializer.Instance.StoreServers(ServerManager.Instance.Servers);
            }).Start();
        }

        public void TrackPerformance(Process p)
        {
            // Track CPU usage
            cpuTracker?.StopThreads();

            cpuTracker = new CPUTracker();
            cpuTracker.TrackTotal(p, this);


            // Track memory usage
            memTracker?.StopThreads();

            memTracker = new MEMTracker();
            memTracker.TrackP(p, this);
        }

        public void CPUValueUpdate(double value)
        {
            cpuValue = value;
            raisePropertyChanged(nameof(CPUValue));
            raisePropertyChanged(nameof(CPUValueRaw));
        }

        public void MemValueUpdate(double value)
        {
            memValue = value;
            raisePropertyChanged(nameof(MemValue));
            raisePropertyChanged(nameof(MemValueRaw));
        }

        public void DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            double bytesIn = double.Parse(e.BytesReceived.ToString());
            double totalBytes = double.Parse(e.TotalBytesToReceive.ToString());
            DownloadProgress = bytesIn / totalBytes * 100;
            DownloadProgressReadable = Math.Round(DownloadProgress, 0) + "%";
        }

        public void DownloadCompletedHandler(object sender, AsyncCompletedEventArgs e)
        {
            DownloadCompleted = true;
        }
        
        public void ServerNameChanged()
        {
            raisePropertyChanged(nameof(ServerTitle));
        }

        private void ConsoleOutChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            raisePropertyChanged(nameof(ConsoleOutList));
        }

        private void WhiteListChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            raisePropertyChanged(nameof(WhiteList));
        }

        private void BanListChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            raisePropertyChanged(nameof(BanList));
        }

        private void OPListChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            raisePropertyChanged(nameof(OPList));
        }


        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void raisePropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }


        private class ActionCommand : ICommand
        {
            private readonly Action _action;

            public ActionCommand(Action action)
            {
                _action = action;
            }

            public void Execute(object parameter)
            {
                _action();
            }

            public bool CanExecute(object parameter)
            {
                return true;
            }

            public event EventHandler CanExecuteChanged;
        }
    }
}