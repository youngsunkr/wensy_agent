﻿//------------------------------------------------------------------------------
// <auto-generated>
//     이 코드는 도구를 사용하여 생성되었습니다.
//     런타임 버전:4.0.30319.42000
//
//     파일 내용을 변경하면 잘못된 동작이 발생할 수 있으며, 코드를 다시 생성하면
//     이러한 변경 내용이 손실됩니다.
// </auto-generated>
//------------------------------------------------------------------------------

// 
// 이 소스 코드가 Microsoft.VSDesigner, 버전 4.0.30319.42000에서 자동으로 생성되었습니다.
// 
#pragma warning disable 1591

namespace WSP.Console.WS_ALERTS {
    using System;
    using System.Web.Services;
    using System.Diagnostics;
    using System.Web.Services.Protocols;
    using System.Xml.Serialization;
    using System.ComponentModel;
    
    
    /// <remarks/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("System.Web.Services", "4.7.2556.0")]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Web.Services.WebServiceBindingAttribute(Name="AlertsAddSoap", Namespace="http://sqlmvp.kr")]
    public partial class AlertsAdd : System.Web.Services.Protocols.SoapHttpClientProtocol {
        
        private System.Threading.SendOrPostCallback AddAlertsOperationCompleted;
        
        private bool useDefaultCredentialsSetExplicitly;
        
        /// <remarks/>
        public AlertsAdd() {
            this.Url = global::WSP.Console.Properties.Settings.Default.WSP_Console_WS_ALERTS_AlertsAdd;
            if ((this.IsLocalFileSystemWebService(this.Url) == true)) {
                this.UseDefaultCredentials = true;
                this.useDefaultCredentialsSetExplicitly = false;
            }
            else {
                this.useDefaultCredentialsSetExplicitly = true;
            }
        }
        
        public new string Url {
            get {
                return base.Url;
            }
            set {
                if ((((this.IsLocalFileSystemWebService(base.Url) == true) 
                            && (this.useDefaultCredentialsSetExplicitly == false)) 
                            && (this.IsLocalFileSystemWebService(value) == false))) {
                    base.UseDefaultCredentials = false;
                }
                base.Url = value;
            }
        }
        
        public new bool UseDefaultCredentials {
            get {
                return base.UseDefaultCredentials;
            }
            set {
                base.UseDefaultCredentials = value;
                this.useDefaultCredentialsSetExplicitly = true;
            }
        }
        
        /// <remarks/>
        public event AddAlertsCompletedEventHandler AddAlertsCompleted;
        
        /// <remarks/>
        [System.Web.Services.Protocols.SoapDocumentMethodAttribute("http://sqlmvp.kr/AddAlerts", RequestNamespace="http://sqlmvp.kr", ResponseNamespace="http://sqlmvp.kr", Use=System.Web.Services.Description.SoapBindingUse.Literal, ParameterStyle=System.Web.Services.Protocols.SoapParameterStyle.Wrapped)]
        public int AddAlerts([System.Xml.Serialization.XmlElementAttribute(DataType="base64Binary")] byte[] bytearr, int iServerNumber, string strTimeIn, string strTimeIn_UTC) {
            object[] results = this.Invoke("AddAlerts", new object[] {
                        bytearr,
                        iServerNumber,
                        strTimeIn,
                        strTimeIn_UTC});
            return ((int)(results[0]));
        }
        
        /// <remarks/>
        public void AddAlertsAsync(byte[] bytearr, int iServerNumber, string strTimeIn, string strTimeIn_UTC) {
            this.AddAlertsAsync(bytearr, iServerNumber, strTimeIn, strTimeIn_UTC, null);
        }
        
        /// <remarks/>
        public void AddAlertsAsync(byte[] bytearr, int iServerNumber, string strTimeIn, string strTimeIn_UTC, object userState) {
            if ((this.AddAlertsOperationCompleted == null)) {
                this.AddAlertsOperationCompleted = new System.Threading.SendOrPostCallback(this.OnAddAlertsOperationCompleted);
            }
            this.InvokeAsync("AddAlerts", new object[] {
                        bytearr,
                        iServerNumber,
                        strTimeIn,
                        strTimeIn_UTC}, this.AddAlertsOperationCompleted, userState);
        }
        
        private void OnAddAlertsOperationCompleted(object arg) {
            if ((this.AddAlertsCompleted != null)) {
                System.Web.Services.Protocols.InvokeCompletedEventArgs invokeArgs = ((System.Web.Services.Protocols.InvokeCompletedEventArgs)(arg));
                this.AddAlertsCompleted(this, new AddAlertsCompletedEventArgs(invokeArgs.Results, invokeArgs.Error, invokeArgs.Cancelled, invokeArgs.UserState));
            }
        }
        
        /// <remarks/>
        public new void CancelAsync(object userState) {
            base.CancelAsync(userState);
        }
        
        private bool IsLocalFileSystemWebService(string url) {
            if (((url == null) 
                        || (url == string.Empty))) {
                return false;
            }
            System.Uri wsUri = new System.Uri(url);
            if (((wsUri.Port >= 1024) 
                        && (string.Compare(wsUri.Host, "localHost", System.StringComparison.OrdinalIgnoreCase) == 0))) {
                return true;
            }
            return false;
        }
    }
    
    /// <remarks/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("System.Web.Services", "4.7.2556.0")]
    public delegate void AddAlertsCompletedEventHandler(object sender, AddAlertsCompletedEventArgs e);
    
    /// <remarks/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("System.Web.Services", "4.7.2556.0")]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    public partial class AddAlertsCompletedEventArgs : System.ComponentModel.AsyncCompletedEventArgs {
        
        private object[] results;
        
        internal AddAlertsCompletedEventArgs(object[] results, System.Exception exception, bool cancelled, object userState) : 
                base(exception, cancelled, userState) {
            this.results = results;
        }
        
        /// <remarks/>
        public int Result {
            get {
                this.RaiseExceptionIfNecessary();
                return ((int)(this.results[0]));
            }
        }
    }
}

#pragma warning restore 1591