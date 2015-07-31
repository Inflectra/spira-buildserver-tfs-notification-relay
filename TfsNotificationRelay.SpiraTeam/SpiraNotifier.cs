using DevCore.TfsNotificationRelay;
using DevCore.TfsNotificationRelay.Configuration;
using DevCore.TfsNotificationRelay.Notifications;
using Microsoft.TeamFoundation.Framework.Server;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.VersionControl.Client;
using Microsoft.TeamFoundation.VersionControl.Server;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Runtime.Serialization;
using DevCore.TfsNotificationRelay.SpiraImportExport;

/**
 * This defines the 'SpiraNotifier' class, which contains methods to
 * send information to the ImportExport Spira server listener 
 * 
 * @author		Bruno Gruber - Inflectra Corporation
 * @version		1.0.0 - August 2015
 *
 */

namespace DevCore.TfsNotificationRelay.SpiraTeam
{
    public class SpiraTeam : INotifier
    {
        const string URL_SUFFIX = "/Services/v4_0/ImportExport.svc";

        public async Task NotifyAsync(TeamFoundationRequestContext requestContext, INotification notification, BotElement bot, EventRuleElement matchingRule)
        {
            string URL = bot.GetSetting("spiraURL");
            string user = bot.GetSetting("spiraUser");
            string password = bot.GetSetting("spiraPassw");
            string projVers = bot.GetSetting("spiraPvers");
            string projNumber = bot.GetSetting("spiraPnumber");
            int status = 0;
            int projectId;

            Int32.TryParse(projNumber, out projectId);
          
            //Check that it is the correct kind of notification
            if (notification is BuildCompletionNotification)
            {
                BuildCompletionNotification buildCompletionNotification = (BuildCompletionNotification)notification;

                if (buildCompletionNotification.IsSuccessful)
                {
                    status = 2;  //sucess
                }
                else if (buildCompletionNotification.BuildStatus.ToString() == "Failed")
                {
                    status = 1; //failed
                }
                else if (buildCompletionNotification.BuildStatus.ToString() == "PartiallySucceeded")
                {
                    status = 3; //unstable
                }
                else if (buildCompletionNotification.BuildStatus.ToString() == "Stopped")
                {
                    status = 4; //aborted
                }
                else
                {
                    TeamFoundationApplicationCore.Log(requestContext, ":: SpiraTeam Plugin for TFS:: The current build finished with a not supported" +
                                                                       " status, please check TFS logs to see detailed information.", 0, EventLogEntryType.Warning);
                }


                DateTime date = buildCompletionNotification.StartTime;
                String sname = buildCompletionNotification.ProjectName + " #" + buildCompletionNotification.BuildNumber;
                String description = ("Information retrieved from TFS : Build requested by " +
                                      buildCompletionNotification.UserName + "<br/> from Team " +
                                      buildCompletionNotification.TeamNames + "<br/> and project collection " +
                                      buildCompletionNotification.TeamProjectCollection + "<br/> for " + sname +
                                      "<br/> with URL " + buildCompletionNotification.BuildUrl +
                                      "<br/> located at " + buildCompletionNotification.DropLocation +
                                      "<br/> Build Started at " + buildCompletionNotification.StartTime +
                                      "<br/> Build finished at " + buildCompletionNotification.FinishTime +
                                      "<br/> Status " + buildCompletionNotification.BuildStatus);
                //List<int> incidentIds;
                var locationService = requestContext.GetService<TeamFoundationLocationService>();
                string baseUrl = String.Format("{0}/", locationService.GetAccessMapping(requestContext,
                                               "PublicAccessMapping").AccessPoint);
                TfsTeamProjectCollection tpc = new TfsTeamProjectCollection(new Uri(baseUrl));
                VersionControlServer sourceControl = tpc.GetService<VersionControlServer>();
                int revisionId = sourceControl.GetLatestChangesetId();

                //SPIRA CODE
                Uri uri = new Uri(URL + URL_SUFFIX);
                ImportExportClient spiraClient = SpiraClientFactory.CreateClient(uri);
                bool success = spiraClient.Connection_Authenticate2(user, password, "TFS Notifier for SpiraTeam v. 1.0.0");
                if (!success)
                {
                    TeamFoundationApplicationCore.Log(requestContext, ":: SpiraTeam Plugin for TFS :: Unable to connect to the Spira server, please verify" +
                                                                      " the provided information in the configuration file.", 0, EventLogEntryType.Error);
                }  
                success = spiraClient.Connection_ConnectToProject(projectId);
                if (!success)
                {
                    TeamFoundationApplicationCore.Log(requestContext, ":: SpiraTeam Plugin for TFS :: The project Id you specified either does not exist," +
                                                                      "or your user does not have access to it! Please verify the configuration file.", 0, EventLogEntryType.Error);
                }                

                RemoteRelease[] releases = spiraClient.Release_Retrieve(true);
                RemoteRelease release = releases.FirstOrDefault(r => r.VersionNumber == projVers);
                if (release != null)
                {
                    List<RemoteBuildSourceCode> revisions = new List<RemoteBuildSourceCode>();
                    RemoteBuildSourceCode revision = new RemoteBuildSourceCode();
                    revision.RevisionKey = revisionId.ToString();
                    revisions.Add(revision);

                    RemoteBuild newBuild = new RemoteBuild();
                    newBuild.Name = sname;
                    newBuild.BuildStatusId = status;
                    newBuild.Description = description;
                    newBuild.CreationDate = date;
                    newBuild.Revisions = revisions.ToArray();
                    newBuild.ReleaseId = release.ReleaseId.Value;
                    spiraClient.Build_Create(newBuild);
                    await Task.Delay(10);
                }
                else
                {
                    TeamFoundationApplicationCore.Log(requestContext, ":: SpiraTeam Plugin for TFS :: The release version number you specified does not " +
                                                                      "exist in the current project! Please verify the configuration file.", 0, EventLogEntryType.Error);
                }

            }

        }
    }
}
