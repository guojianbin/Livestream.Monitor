﻿using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using Livestream.Monitor.ViewModels;

namespace Livestream.Monitor.Model
{
    public class StreamLauncher
    {
        private readonly IMonitorStreamsModel monitorStreamsModel;
        private readonly ISettingsHandler settingsHandler;
        private readonly IWindowManager windowManager;

        public StreamLauncher(
            IMonitorStreamsModel monitorStreamsModel,
            ISettingsHandler settingsHandler,
            IWindowManager windowManager)
        {
            if (monitorStreamsModel == null) throw new ArgumentNullException(nameof(monitorStreamsModel));
            if (settingsHandler == null) throw new ArgumentNullException(nameof(settingsHandler));
            if (windowManager == null) throw new ArgumentNullException(nameof(windowManager));

            this.monitorStreamsModel = monitorStreamsModel;
            this.settingsHandler = settingsHandler;
            this.windowManager = windowManager;
        }

        public void StartStream()
        {
            var livestreamPath = settingsHandler.Settings.LivestreamerFullPath;
            if (!File.Exists(livestreamPath))
            {
                var msgBox = new MessageBoxViewModel()
                {
                    DisplayName = "Livestreamer not found",
                    MessageText = $"Could not find livestreamer @ {livestreamPath}.{Environment.NewLine} Please download and install livestreamer from 'http://docs.livestreamer.io/install.html#windows-binaries'"
                };
                var settings = new WindowSettingsBuilder().SizeToContent()
                                                      .WithWindowStyle(WindowStyle.ToolWindow)
                                                      .WithResizeMode(ResizeMode.NoResize)
                                                      .Create();
                windowManager.ShowWindow(msgBox, null, settings);
                return;
            }

            var selectedChannel = monitorStreamsModel.SelectedChannel;
            if (selectedChannel == null || !selectedChannel.Live) return;

            // Fall back to source stream quality for non-partnered Channels
            var streamQuality = (!selectedChannel.IsPartner &&
                                 settingsHandler.Settings.DefaultStreamQuality != StreamQuality.Source)
                                    ? StreamQuality.Source
                                    : settingsHandler.Settings.DefaultStreamQuality;

            string livestreamerArgs = $"http://www.twitch.tv/{selectedChannel.ChannelName}/ {streamQuality}";
            var messageBoxViewModel = ShowStreamLoadMessageBox(selectedChannel, settingsHandler.Settings.DefaultStreamQuality);

            // the process needs to be launched from its own thread so it doesn't lockup the UI
            Task.Run(() =>
            {
                var proc = new Process
                {
                    StartInfo =
                    {
                        FileName = livestreamPath,
                        Arguments = livestreamerArgs,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    },
                    EnableRaisingEvents = true
                };

                bool preventClose = false;

                // see below for output handler
                proc.ErrorDataReceived +=
                    (sender, args) =>
                    {
                        if (args.Data != null)
                        {
                            preventClose = true;
                            messageBoxViewModel.MessageText += Environment.NewLine + args.Data;
                        }
                    };
                proc.OutputDataReceived +=
                    (sender, args) =>
                    {
                        if (args.Data != null) messageBoxViewModel.MessageText += Environment.NewLine + args.Data;
                    };

                try
                {
                    proc.Start();

                    proc.BeginErrorReadLine();
                    proc.BeginOutputReadLine();

                    proc.WaitForExit();
                    if (proc.ExitCode != 0) preventClose = true;
                }
                catch (Exception)
                {
                    // TODO log errors opening stream
                }

                if (preventClose)
                {
                    messageBoxViewModel.MessageText += Environment.NewLine + Environment.NewLine +
                                                       "ERROR occured in Livestreamer: Manually close this window when you've finished reading the livestreamer output.";
                }
                else
                    messageBoxViewModel.TryClose();
            });
        }

        private MessageBoxViewModel ShowStreamLoadMessageBox(ChannelData selectedChannel, StreamQuality streamQuality)
        {
            var messageBoxViewModel = new MessageBoxViewModel
            {
                DisplayName = $"Stream '{selectedChannel.ChannelName}'",
                MessageText = "Launching livestreamer..."
            };

            // Notify the user if the quality has been swapped back to source due to the channel not being partenered.
            if (!selectedChannel.IsPartner && streamQuality != StreamQuality.Source)
            {
                messageBoxViewModel.MessageText += Environment.NewLine + "[NOTE] Channel is not a twitch partner so falling back to Source quality";
            }

            var settings = new WindowSettingsBuilder().SizeToContent()
                                                      .WithWindowStyle(WindowStyle.ToolWindow)
                                                      .WithResizeMode(ResizeMode.NoResize)
                                                      .Create();

            windowManager.ShowWindow(messageBoxViewModel, null, settings);
            return messageBoxViewModel;
        }
    }
}
