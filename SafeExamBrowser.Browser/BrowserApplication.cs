﻿/*
 * Copyright (c) 2019 ETH Zürich, Educational Development and Technology (LET)
 * 
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using CefSharp;
using CefSharp.WinForms;
using SafeExamBrowser.Applications.Contracts;
using SafeExamBrowser.Applications.Contracts.Events;
using SafeExamBrowser.Applications.Contracts.Resources.Icons;
using SafeExamBrowser.Browser.Contracts;
using SafeExamBrowser.Browser.Contracts.Events;
using SafeExamBrowser.Browser.Events;
using SafeExamBrowser.Configuration.Contracts;
using SafeExamBrowser.I18n.Contracts;
using SafeExamBrowser.Logging.Contracts;
using SafeExamBrowser.Settings.Browser;
using SafeExamBrowser.Settings.Logging;
using SafeExamBrowser.UserInterface.Contracts;
using SafeExamBrowser.UserInterface.Contracts.MessageBox;
using BrowserSettings = SafeExamBrowser.Settings.Browser.BrowserSettings;

namespace SafeExamBrowser.Browser
{
	public class BrowserApplication : IBrowserApplication
	{
		private int instanceIdCounter = default(int);

		private AppConfig appConfig;
		private List<BrowserApplicationInstance> instances;
		private IMessageBox messageBox;
		private IModuleLogger logger;
		private BrowserSettings settings;
		private IText text;
		private IUserInterfaceFactory uiFactory;

		public bool AutoStart { get; private set; }
		public IconResource Icon { get; private set; }
		public Guid Id { get; private set; }
		public string Name { get; private set; }
		public string Tooltip { get; private set; }

		public event DownloadRequestedEventHandler ConfigurationDownloadRequested;
		public event WindowsChangedEventHandler WindowsChanged;

		public BrowserApplication(
			AppConfig appConfig,
			BrowserSettings settings,
			IMessageBox messageBox,
			IModuleLogger logger,
			IText text,
			IUserInterfaceFactory uiFactory)
		{
			this.appConfig = appConfig;
			this.instances = new List<BrowserApplicationInstance>();
			this.logger = logger;
			this.messageBox = messageBox;
			this.settings = settings;
			this.text = text;
			this.uiFactory = uiFactory;
		}

		public IEnumerable<IApplicationWindow> GetWindows()
		{
			return new List<IApplicationWindow>(instances);
		}

		public void Initialize()
		{
			var cefSettings = InitializeCefSettings();
			var success = Cef.Initialize(cefSettings, true, default(IApp));

			InitializeApplicationInfo();

			if (success)
			{
				logger.Info("Initialized browser.");
			}
			else
			{
				throw new Exception("Failed to initialize browser!");
			}
		}

		public void Start()
		{
			CreateNewInstance();
		}

		public void Terminate()
		{
			foreach (var instance in instances)
			{
				instance.Terminated -= Instance_Terminated;
				instance.Terminate();
				logger.Info($"Terminated browser instance {instance.Id}.");
			}

			Cef.Shutdown();
			logger.Info("Terminated browser.");
		}

		private void InitializeApplicationInfo()
		{
			AutoStart = true;
			Icon = new BrowserIconResource();
			Id = Guid.NewGuid();
			Name = text.Get(TextKey.Browser_Name);
			Tooltip = text.Get(TextKey.Browser_Tooltip);
		}

		private void CreateNewInstance(string url = null)
		{
			var id = ++instanceIdCounter;
			var isMainInstance = instances.Count == 0;
			var instanceLogger = logger.CloneFor($"Browser Instance #{id}");
			var startUrl = url ?? settings.StartUrl;
			var instance = new BrowserApplicationInstance(appConfig, settings, id, isMainInstance, messageBox, instanceLogger, text, uiFactory, startUrl);

			instance.ConfigurationDownloadRequested += (fileName, args) => ConfigurationDownloadRequested?.Invoke(fileName, args);
			instance.PopupRequested += Instance_PopupRequested;
			instance.Terminated += Instance_Terminated;

			instance.Initialize();
			instances.Add(instance);

			logger.Info($"Created browser instance {instance.Id}.");
			WindowsChanged?.Invoke();
		}

		private CefSettings InitializeCefSettings()
		{
			var warning = logger.LogLevel == LogLevel.Warning;
			var error = logger.LogLevel == LogLevel.Error;
			var cefSettings = new CefSettings
			{
				CachePath = appConfig.BrowserCachePath,
				LogFile = appConfig.BrowserLogFilePath,
				LogSeverity = error ? LogSeverity.Error : (warning ? LogSeverity.Warning : LogSeverity.Info),
				UserAgent = InitializeUserAgent()
			};

			InitializeProxySettings(cefSettings);

			cefSettings.CefCommandLineArgs.Add("touch-events", "enabled");

			logger.Debug($"Cache path: {cefSettings.CachePath}");
			logger.Debug($"Engine version: Chromium {Cef.ChromiumVersion}, CEF {Cef.CefVersion}, CefSharp {Cef.CefSharpVersion}");
			logger.Debug($"Log file: {cefSettings.LogFile}");
			logger.Debug($"Log severity: {cefSettings.LogSeverity}");

			return cefSettings;
		}

		private void InitializeProxySettings(CefSettings cefSettings)
		{
			if (settings.Proxy.Policy == ProxyPolicy.Custom)
			{
				if (settings.Proxy.AutoConfigure)
				{
					cefSettings.CefCommandLineArgs.Add("proxy-pac-url", settings.Proxy.AutoConfigureUrl);
				}

				if (settings.Proxy.AutoDetect)
				{
					cefSettings.CefCommandLineArgs.Add("proxy-auto-detect", "");
				}

				if (settings.Proxy.BypassList.Any())
				{
					cefSettings.CefCommandLineArgs.Add("proxy-bypass-list", string.Join(";", settings.Proxy.BypassList));
				}

				if (settings.Proxy.Proxies.Any())
				{
					var proxies = new List<string>();

					foreach (var proxy in settings.Proxy.Proxies)
					{
						proxies.Add($"{ToScheme(proxy.Protocol)}={proxy.Host}:{proxy.Port}");
					}

					cefSettings.CefCommandLineArgs.Add("proxy-server", string.Join(";", proxies));
				}
			}
		}

		private string ToScheme(ProxyProtocol protocol)
		{
			switch (protocol)
			{
				case ProxyProtocol.Ftp:
					return Uri.UriSchemeFtp;
				case ProxyProtocol.Http:
					return Uri.UriSchemeHttp;
				case ProxyProtocol.Https:
					return Uri.UriSchemeHttps;
				case ProxyProtocol.Socks:
					return "socks";
			}

			throw new NotImplementedException($"Mapping for proxy protocol '{protocol}' is not yet implemented!");
		}

		private void Instance_PopupRequested(PopupRequestedEventArgs args)
		{
			logger.Info($"Received request to create new instance for '{args.Url}'...");
			CreateNewInstance(args.Url);
		}

		private void Instance_Terminated(int id)
		{
			instances.Remove(instances.First(i => i.Id == id));
			WindowsChanged?.Invoke();
		}

		/// <summary>
		/// TODO: Workaround to correctly set the user agent due to missing support for request interception for requests made by service workers.
		///       Remove once CEF fully supports service workers and reactivate the functionality in <see cref="Handlers.RequestHandler"/>!
		/// </summary>
		private string InitializeUserAgent()
		{
			var osVersion = $"{Environment.OSVersion.Version.Major}.{Environment.OSVersion.Version.Minor}";
			var sebVersion = $"SEB/{appConfig.ProgramInformationalVersion}";

			if (settings.UseCustomUserAgent)
			{
				return $"{settings.CustomUserAgent} {sebVersion}";
			}
			else
			{
				return $"Mozilla/5.0 (Windows NT {osVersion}) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{Cef.ChromiumVersion} {sebVersion}";
			}
		}
	}
}
