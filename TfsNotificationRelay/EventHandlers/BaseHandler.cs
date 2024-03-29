﻿/*
 * TfsNotificationRelay - http://github.com/kria/TfsNotificationRelay
 * 
 * Copyright (C) 2014 Kristian Adrup
 * 
 * This file is part of TfsNotificationRelay.
 * 
 * TfsNotificationRelay is free software: you can redistribute it and/or 
 * modify it under the terms of the GNU General Public License as published 
 * by the Free Software Foundation, either version 3 of the License, or 
 * (at your option) any later version. See included file COPYING for details.
 */

using DevCore.TfsNotificationRelay.Notifications;
using Microsoft.TeamFoundation.Framework.Server;
using Microsoft.TeamFoundation.Integration.Server;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Configuration;
using DevCore.TfsNotificationRelay.Configuration;
using Microsoft.TeamFoundation.Server.Core;
using Microsoft.VisualStudio.Services.Identity;
using Microsoft.TeamFoundation.Framework.Common;

namespace DevCore.TfsNotificationRelay.EventHandlers
{
    public abstract class BaseHandler<T> : BaseHandler
    {
       
        private const string area = "TfsNotificationRelay";

        public override Type[] SubscribedTypes()
        {
            return new Type[] { typeof(T) };
        }

        protected override IEnumerable<INotification> CreateNotifications(TeamFoundationRequestContext requestContext, object notificationEventArgs, int maxLines)
        {
            return CreateNotifications(requestContext, (T)notificationEventArgs, maxLines);
        }
       
        protected abstract IEnumerable<INotification> CreateNotifications(TeamFoundationRequestContext requestContext, T notificationEventArgs, int maxLines);
    }

    public abstract class BaseHandler : ISubscriber
    {
        
        protected static Configuration.SettingsElement settings;
        private static IDictionary<string, string> projectsNames;

        public string Name
        {
            get { return "TfsNotificationRelay"; }
        }

        public SubscriberPriority Priority
        {
            get { return SubscriberPriority.Normal; }
        }

        public IDictionary<string, string> ProjectsNames
        {
            get { return projectsNames; }
        }

        public abstract Type[] SubscribedTypes();

        protected abstract IEnumerable<INotification> CreateNotifications(TeamFoundationRequestContext requestContext, object notificationEventArgs, int maxLines);

        public virtual EventNotificationStatus ProcessEvent(TeamFoundationRequestContext requestContext, NotificationType notificationType,
            object notificationEventArgs, out int statusCode, out string statusMessage, out Microsoft.TeamFoundation.Common.ExceptionPropertyCollection properties)
        {
                       
            requestContext.Trace(0, TraceLevel.Verbose, Constants.TraceArea, "BaseHandler", 
                "ProcessEvent: notificationType={0}, notificationEventArgs={1}", notificationType, notificationEventArgs);
 
            Stopwatch timer = new Stopwatch();
            timer.Start();
                        
            try
            {
                
                settings = Configuration.TfsNotificationRelaySection.Instance.Settings;
                statusCode = 0;
                statusMessage = string.Empty;
                properties = null;

                if (projectsNames == null)
                {
                    var commonService = requestContext.GetService<CommonStructureService>();
                    projectsNames = commonService.GetProjects(requestContext).ToDictionary(p => p.Uri, p => p.Name);
                }

                var config = Configuration.TfsNotificationRelaySection.Instance;

                if (notificationType == NotificationType.Notification)
                {
               
                    var notifications = CreateNotifications(requestContext, notificationEventArgs, settings.MaxLines).ToList();

                    var tasks = new List<Task>();

                    foreach (var bot in config.Bots)
                    {
                        string folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().CodeBase);
                        Uri format = new Uri(folder);
                        string formatFolder = format.LocalPath;
                        
                        string spiraAssembly = Path.Combine(formatFolder, "DevCore.TfsNotificationRelay.SpiraTeam.dll");
                        Type[] types = Assembly.LoadFrom(spiraAssembly).GetExportedTypes();
                        Type botType = types.FirstOrDefault(t => t.FullName == "DevCore.TfsNotificationRelay.SpiraTeam.SpiraTeam"); 
                        //String msg = (botType.ToString() + "!");
                        //TeamFoundationApplicationCore.Log(requestContext, msg, 0, EventLogEntryType.Warning);
                        if (botType != null)
                        {
                            try
                            {
                                var notifier = (INotifier)Activator.CreateInstance(botType);
                                foreach (var notification in notifications)
                                {
                                    var matchingRule = notification.GetRuleMatch(requestContext.ServiceHost.Name, bot.EventRules);
                                    if (matchingRule != null && matchingRule.Notify)
                                        tasks.Add(NotifyAsync(requestContext, notifier, notification, bot, matchingRule));
                                }
                                
                            }
                            catch (Exception ex)
                            {
                                TeamFoundationApplicationCore.LogException(requestContext, String.Format("TfsNotificationRelay: Can't create bot {0}.", bot.Id), ex);
                            }
                        }
                        else
                        {
                            string errmsg = String.Format("TfsNotificationRelay: Unknown bot type: {0}, Skipping bot: {1}.", bot.Type, bot.Id);
                            TeamFoundationApplicationCore.Log(requestContext, errmsg, 0, EventLogEntryType.Error);
                        }
                    }

                    Task.WaitAll(tasks.ToArray());
                }

                return EventNotificationStatus.ActionPermitted;
            }
            finally
            {
                timer.Stop();
                requestContext.Trace(0, TraceLevel.Verbose, Constants.TraceArea, "BaseHandler", "Time spent in ProcessEvent: {0}", timer.Elapsed);
            }
            
        }

        private async Task NotifyAsync(TeamFoundationRequestContext requestContext, INotifier notifier, INotification notification, Configuration.BotElement bot, EventRuleElement matchingRule)
        {
            try
            {
                await notifier.NotifyAsync(requestContext, notification, bot, matchingRule);
            }
            catch (Exception ex)
            {
                TeamFoundationApplicationCore.LogException(requestContext, String.Format("TfsNotificationRelay: Notify failed for bot {0}.", bot.Id), ex, 0, EventLogEntryType.Error);
            }
        }


        /// <summary>
        /// Gets the team names by project uri and user identity
        /// </summary>
        /// <param name="requestContext"></param>
        /// <param name="projectUri"></param>
        /// <param name="identity"></param>
        /// <returns>The teams the user is a member of</returns>
        protected IEnumerable<string> GetUserTeamsByProjectUri(TeamFoundationRequestContext requestContext, string projectUri, IdentityDescriptor identity)
        {
            var teamService = requestContext.GetService<TeamFoundationTeamService>();

            var projectTeams = teamService.QueryTeams(requestContext, projectUri);
            Trace(requestContext, "Teams in project {0}: {1}", projectUri, String.Join(", ", projectTeams.Select(t => t.Name)));

            var userTeams = teamService.QueryTeams(requestContext, identity);
            Trace(requestContext, "Teams for user {0}: {1}", identity, String.Join(", ", userTeams.Select(t => t.Name)));

            var teamNames = projectTeams.Join(userTeams,
                pt => pt.Identity.Descriptor,
                ut => ut.Identity.Descriptor,
                (pt, ut) => pt.Name, IdentityDescriptorComparer.Instance);

            return teamNames;
        }

        /// <summary>
        /// Gets the team names by project name and user identity
        /// </summary>
        /// <param name="requestContext"></param>
        /// <param name="projectName"></param>
        /// <param name="identity"></param>
        /// <returns>The teams the user is a member of</returns>
        protected IEnumerable<string> GetUserTeamsByProjectName(TeamFoundationRequestContext requestContext, string projectName, IdentityDescriptor identity)
        {
            var projectUri = this.ProjectsNames.Where(p => p.Value ==  projectName)
                .Select(p => p.Key).FirstOrDefault();
            if (projectUri == null) return Enumerable.Empty<string>();

            return GetUserTeamsByProjectUri(requestContext, projectUri, identity);
        }

        /// <summary>
        /// Gets the team names by project name and username
        /// </summary>
        /// <param name="requestContext"></param>
        /// <param name="projectName"></param>
        /// <param name="username"></param>
        /// <returns>The teams the user is a member of</returns>
        protected IEnumerable<string> GetUserTeamsByProjectName(TeamFoundationRequestContext requestContext, string projectName, string username)
        {
            var identity = GetIdentyByUsername(requestContext, username);
            if (identity == null) return Enumerable.Empty<string>();

            return GetUserTeamsByProjectName(requestContext, projectName, identity.Descriptor);
        }

        protected TeamFoundationIdentity GetIdentyByUsername(TeamFoundationRequestContext requestContext, string username)
        {
            var identityService = requestContext.GetService<TeamFoundationIdentityService>();
            var identity = identityService.ReadIdentity(requestContext, IdentitySearchFactor.AccountName, username);

            return identity;
        }

        protected void Trace(TeamFoundationRequestContext requestContext, string format, params object[] args)
        {
            requestContext.Trace(0, TraceLevel.Verbose, Constants.TraceArea, this.GetType().Name, format, args);
        }

    }

}
