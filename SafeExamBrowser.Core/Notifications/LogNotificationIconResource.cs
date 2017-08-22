﻿/*
 * Copyright (c) 2017 ETH Zürich, Educational Development and Technology (LET)
 * 
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using System;
using SafeExamBrowser.Contracts.Configuration;

namespace SafeExamBrowser.Core.Notifications
{
	class LogNotificationIconResource : IIconResource
	{
		public Uri Uri => new Uri("pack://application:,,,/SafeExamBrowser.UserInterface.Windows10;component/Images/LogNotification.ico");
		public bool IsBitmapResource => true;
		public bool IsXamlResource => false;
	}
}