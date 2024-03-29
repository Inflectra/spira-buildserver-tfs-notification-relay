﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ServiceModel;
using System.ServiceModel.Description;
using System.Xml.Linq;
using System.Runtime.Serialization;

namespace DevCore.TfsNotificationRelay.SpiraTeam
{
    /// <summary>
    /// Factory for creating and configuring new Spira API endpoints
    /// </summary>
    public static class SpiraClientFactory
    {
        /// <summary>
        /// Creates the WCF endpoints
        /// </summary>
        /// <param name="fullUri">The URI</param>
        /// <returns>The client class</returns>
        /// <remarks>We need to do this in code because the app.config file is not available in VSTO</remarks>
        public static SpiraImportExport.ImportExportClient CreateClient(Uri fullUri)
        {
            //Configure the binding
            BasicHttpBinding httpBinding = new BasicHttpBinding();

            //Allow cookies and large messages
            httpBinding.AllowCookies = true;
            httpBinding.MaxBufferSize = 100000000; //100MB
            httpBinding.MaxReceivedMessageSize = 100000000; //100MB
            httpBinding.ReceiveTimeout = new TimeSpan(0, 10, 0);    //10 Minutes
            httpBinding.SendTimeout = new TimeSpan(0, 10, 0);    //10 Minutes
            httpBinding.ReaderQuotas.MaxStringContentLength = 2147483647;
            httpBinding.ReaderQuotas.MaxDepth = 2147483647;
            httpBinding.ReaderQuotas.MaxBytesPerRead = 2147483647;
            httpBinding.ReaderQuotas.MaxNameTableCharCount = 2147483647;
            httpBinding.ReaderQuotas.MaxArrayLength = 2147483647;

            //Handle SSL if necessary
            if (fullUri.Scheme == "https")
            {
                httpBinding.Security.Mode = BasicHttpSecurityMode.Transport;
                httpBinding.Security.Transport.ClientCredentialType = HttpClientCredentialType.None;

                //Allow self-signed certificates
                PermissiveCertificatePolicy.Enact("");
            }
            else
            {
                httpBinding.Security.Mode = BasicHttpSecurityMode.None;
            }

            //Create the new client with endpoint and HTTP Binding
            EndpointAddress endpointAddress = new EndpointAddress(fullUri.AbsoluteUri);
            SpiraImportExport.ImportExportClient spiraImportExport = new SpiraImportExport.ImportExportClient(httpBinding, endpointAddress);

            //Modify the operation behaviors to allow unlimited objects in the graph
            foreach (var operation in spiraImportExport.Endpoint.Contract.Operations)
            {
                var behavior = operation.Behaviors.Find<DataContractSerializerOperationBehavior>() as DataContractSerializerOperationBehavior;
                if (behavior != null)
                {
                    behavior.MaxItemsInObjectGraph = 2147483647;
                }
            }

            return spiraImportExport;
        }
    }
}
