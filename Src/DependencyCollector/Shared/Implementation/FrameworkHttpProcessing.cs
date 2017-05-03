﻿#if !NET40
namespace Microsoft.ApplicationInsights.DependencyCollector.Implementation
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Net;
    using Microsoft.ApplicationInsights.DataContracts;
    using Microsoft.ApplicationInsights.DependencyCollector.Implementation.Operation;
    using Microsoft.ApplicationInsights.Extensibility;
    using Microsoft.ApplicationInsights.Extensibility.Implementation;
    using Microsoft.ApplicationInsights.Web.Implementation;

    /// <summary>
    /// Concrete class with all processing logic to generate RDD data from the callbacks received from FrameworkHttpEventListener.
    /// </summary>
    internal sealed class FrameworkHttpProcessing : HttpProcessing
    {
        internal CacheBasedOperationHolder TelemetryTable;
        private readonly ApplicationInsightsUrlFilter applicationInsightsUrlFilter;

        internal FrameworkHttpProcessing(TelemetryConfiguration configuration, CacheBasedOperationHolder telemetryTupleHolder, bool setCorrelationHeaders, ICollection<string> correlationDomainExclusionList, string appIdEndpoint)
            : base(configuration, SdkVersionUtils.GetSdkVersion("rdd" + RddSource.Framework + ":"), null, setCorrelationHeaders, correlationDomainExclusionList, appIdEndpoint)
        {
            if (telemetryTupleHolder == null)
            {
                throw new ArgumentNullException("telemetryTupleHolder");
            }

            this.applicationInsightsUrlFilter = new ApplicationInsightsUrlFilter(configuration);
            this.TelemetryTable = telemetryTupleHolder;
        }
      
        /// <summary>
        /// On begin callback from Framework event source.
        /// </summary>
        /// <param name="id">This object.</param>
        /// <param name="resourceName">URI of the web request.</param>
        public void OnBeginHttpCallback(long id, string resourceName)
        {
            try
            {
                DependencyCollectorEventSource.Log.BeginCallbackCalled(id, resourceName);
                if (DependencyTableStore.Instance.IsDesktopHttpDiagnosticSourceActivated)
                {
                    // request is handled by Desktop DiagnosticSource Listener
                    DependencyCollectorEventSource.Log.TrackingAnExistingTelemetryItemVerbose();
                    return;
                }

                if (string.IsNullOrEmpty(resourceName))
                {
                    DependencyCollectorEventSource.Log.NotExpectedCallback(id, "OnBeginHttp", "resourceName is empty");
                    return;
                }

                if (this.applicationInsightsUrlFilter.IsApplicationInsightsUrl(resourceName))
                {
                    return;
                }

                Uri url;
                try
                {
                    url = new Uri(resourceName);
                }
                catch (UriFormatException)
                {
                    DependencyCollectorEventSource.Log.NotExpectedCallback(id, "OnBeginHttp", "resourceName is not a URL " + resourceName);
                    return;
                }

                var telemetryTuple = this.TelemetryTable.Get(id);
                if (telemetryTuple != null)
                {
                    // This operation is already being tracked. It may have started by a previous
                    // call on OnBeginHttpCallback or OnRequestSend. We will not restart operation
                    if (telemetryTuple.Item1 != null)
                    {
                        DependencyCollectorEventSource.Log.TrackingAnExistingTelemetryItemVerbose();
                        return;
                    }
                }

                bool isCustomCreated = false;

                var telemetry = ClientServerDependencyTracker.BeginTracking(this.telemetryClient);

                telemetry.Type = RemoteDependencyConstants.HTTP;
                telemetry.Name = url.AbsolutePath;
                telemetry.Target = DependencyTargetNameHelper.GetDependencyTargetName(url);
                telemetry.Data = url.OriginalString;

                this.TelemetryTable.Store(id, new Tuple<DependencyTelemetry, bool>(telemetry, isCustomCreated));
            }
            catch (Exception exception)
            {
                DependencyCollectorEventSource.Log.CallbackError(id, "OnBeginHttp", exception);
            }
        }
        
        /// <summary>
        /// On end callback from Framework event source.
        /// </summary>
        /// <param name="id">The id.</param>
        /// <param name="success">The success to indicate if the dependency call completed successfully or not.</param>
        /// <param name="synchronous">The synchronous flag to indicate if the dependency call was synchronous or not.</param>
        /// <param name="statusCode">The HTTP status code of the response.</param>
        public void OnEndHttpCallback(long id, bool? success, bool synchronous, int? statusCode)
        {
            DependencyCollectorEventSource.Log.EndCallbackCalled(id.ToString(CultureInfo.InvariantCulture));
            var telemetryTuple = this.TelemetryTable.Get(id);

            if (telemetryTuple == null)
            {
                DependencyCollectorEventSource.Log.EndCallbackWithNoBegin(id.ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (DependencyTableStore.Instance.IsDesktopHttpDiagnosticSourceActivated && success.HasValue && success.Value)
            {
                // DesktopHttpDiagnosticSource 
            }

            if (!telemetryTuple.Item2)
            {
                this.TelemetryTable.Remove(id);
                DependencyTelemetry telemetry = telemetryTuple.Item1;

                if (statusCode.HasValue)
                {
                    // We calculate success on the base of http code and do not use the 'success' method argument
                    // because framework returns true all the time if you use HttpClient to create a request
                    // statusCode == -1 if there is no Response
                    telemetry.Success = (statusCode > 0) && (statusCode < 400);
                    telemetry.ResultCode = statusCode.Value > 0 ? statusCode.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;

                    ClientServerDependencyTracker.EndTracking(this.telemetryClient, telemetry);
                }
                else
                {
                    // This case is for 4.5.2
                    // We never collected statusCode or success before 2.1.0-beta4
                    // We also had duplicates if runtime is also 4.5.2 (4.6 runtime has no such problem)
                    // So starting with 2.1.0-beta4 we are cutting support for HTTP dependencies in .NET 4.5.2.
                }
            }
        }

        /// <summary>
        /// Implemented by the derived class for adding the tuple to its specific cache.
        /// </summary>
        /// <param name="webRequest">The request which acts the key.</param>
        /// <param name="telemetry">The dependency telemetry for the tuple.</param>
        /// <param name="isCustomCreated">Boolean value that tells if the current telemetry item is being added by the customer or not.</param>
        protected override void AddTupleForWebDependencies(WebRequest webRequest, DependencyTelemetry telemetry, bool isCustomCreated)
        {
            var telemetryTuple = new Tuple<DependencyTelemetry, bool>(telemetry, isCustomCreated);
            this.TelemetryTable.Store(ClientServerDependencyTracker.GetIdForRequestObject(webRequest), telemetryTuple);
        }

        /// <summary>
        /// Implemented by the derived class for getting the tuple from its specific cache.
        /// </summary>
        /// <param name="webRequest">The request which acts as the key.</param>
        /// <returns>The tuple for the given request.</returns>
        protected override Tuple<DependencyTelemetry, bool> GetTupleForWebDependencies(WebRequest webRequest)
        {
            return this.TelemetryTable.Get(ClientServerDependencyTracker.GetIdForRequestObject(webRequest));
        }

        /// <summary>
        /// Implemented by the derived class for removing the tuple from its specific cache.
        /// </summary>
        /// <param name="webRequest">The request which acts as the key.</param>
        protected override void RemoveTupleForWebDependencies(WebRequest webRequest)
        {
            this.TelemetryTable.Remove(ClientServerDependencyTracker.GetIdForRequestObject(webRequest));
        }
    }
}
#endif