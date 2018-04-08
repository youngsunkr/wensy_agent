﻿//------------------------------------------------------------------------------
// <auto-generated>
//     이 코드는 도구를 사용하여 생성되었습니다.
//     런타임 버전:4.0.30319.18444
//
//     파일 내용을 변경하면 잘못된 동작이 발생할 수 있으며, 코드를 다시 생성하면
//     이러한 변경 내용이 손실됩니다.
// </auto-generated>
//------------------------------------------------------------------------------

// 
// 이 소스 코드가 Microsoft.VSDesigner, 버전 4.0.30319.18444에서 자동으로 생성되었습니다.
// 
#pragma warning disable 1591

namespace SHST.Service.Agent.WS_AGENT {
    using System;
    using System.Web.Services;
    using System.Diagnostics;
    using System.Web.Services.Protocols;
    using System.Xml.Serialization;
    using System.ComponentModel;
    
    
    /// <remarks/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("System.Web.Services", "4.0.30319.18408")]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Web.Services.WebServiceBindingAttribute(Name="CheckDuplicatedClientSoap", Namespace="http://tempuri.org/")]
    public partial class CheckDuplicatedClient : System.Web.Services.Protocols.SoapHttpClientProtocol {
        
        private System.Threading.SendOrPostCallback IsValidClientOperationCompleted;
        
        private System.Threading.SendOrPostCallback DeleteRegistedAgentOperationCompleted;
        
        private bool useDefaultCredentialsSetExplicitly;
        
        /// <remarks/>
        public CheckDuplicatedClient() {
            this.Url = global::SHST.Service.Agent.Properties.Settings.Default.SP_Client_Agent_WS_AGENT_CheckDuplicatedClient;
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
        public event IsValidClientCompletedEventHandler IsValidClientCompleted;
        
        /// <remarks/>
        public event DeleteRegistedAgentCompletedEventHandler DeleteRegistedAgentCompleted;
        
        /// <remarks/>
        [System.Web.Services.Protocols.SoapDocumentMethodAttribute("http://tempuri.org/IsValidClient", RequestNamespace="http://tempuri.org/", ResponseNamespace="http://tempuri.org/", Use=System.Web.Services.Description.SoapBindingUse.Literal, ParameterStyle=System.Web.Services.Protocols.SoapParameterStyle.Wrapped)]
        public bool IsValidClient(string strProductKey, string strMachineKey, string strIP) {
            object[] results = this.Invoke("IsValidClient", new object[] {
                        strProductKey,
                        strMachineKey,
                        strIP});
            return ((bool)(results[0]));
        }
        
        /// <remarks/>
        public void IsValidClientAsync(string strProductKey, string strMachineKey, string strIP) {
            this.IsValidClientAsync(strProductKey, strMachineKey, strIP, null);
        }
        
        /// <remarks/>
        public void IsValidClientAsync(string strProductKey, string strMachineKey, string strIP, object userState) {
            if ((this.IsValidClientOperationCompleted == null)) {
                this.IsValidClientOperationCompleted = new System.Threading.SendOrPostCallback(this.OnIsValidClientOperationCompleted);
            }
            this.InvokeAsync("IsValidClient", new object[] {
                        strProductKey,
                        strMachineKey,
                        strIP}, this.IsValidClientOperationCompleted, userState);
        }
        
        private void OnIsValidClientOperationCompleted(object arg) {
            if ((this.IsValidClientCompleted != null)) {
                System.Web.Services.Protocols.InvokeCompletedEventArgs invokeArgs = ((System.Web.Services.Protocols.InvokeCompletedEventArgs)(arg));
                this.IsValidClientCompleted(this, new IsValidClientCompletedEventArgs(invokeArgs.Results, invokeArgs.Error, invokeArgs.Cancelled, invokeArgs.UserState));
            }
        }
        
        /// <remarks/>
        [System.Web.Services.Protocols.SoapDocumentMethodAttribute("http://tempuri.org/DeleteRegistedAgent", RequestNamespace="http://tempuri.org/", ResponseNamespace="http://tempuri.org/", Use=System.Web.Services.Description.SoapBindingUse.Literal, ParameterStyle=System.Web.Services.Protocols.SoapParameterStyle.Wrapped)]
        public bool DeleteRegistedAgent(string strProductKey, string strMachineKey, string strIP) {
            object[] results = this.Invoke("DeleteRegistedAgent", new object[] {
                        strProductKey,
                        strMachineKey,
                        strIP});
            return ((bool)(results[0]));
        }
        
        /// <remarks/>
        public void DeleteRegistedAgentAsync(string strProductKey, string strMachineKey, string strIP) {
            this.DeleteRegistedAgentAsync(strProductKey, strMachineKey, strIP, null);
        }
        
        /// <remarks/>
        public void DeleteRegistedAgentAsync(string strProductKey, string strMachineKey, string strIP, object userState) {
            if ((this.DeleteRegistedAgentOperationCompleted == null)) {
                this.DeleteRegistedAgentOperationCompleted = new System.Threading.SendOrPostCallback(this.OnDeleteRegistedAgentOperationCompleted);
            }
            this.InvokeAsync("DeleteRegistedAgent", new object[] {
                        strProductKey,
                        strMachineKey,
                        strIP}, this.DeleteRegistedAgentOperationCompleted, userState);
        }
        
        private void OnDeleteRegistedAgentOperationCompleted(object arg) {
            if ((this.DeleteRegistedAgentCompleted != null)) {
                System.Web.Services.Protocols.InvokeCompletedEventArgs invokeArgs = ((System.Web.Services.Protocols.InvokeCompletedEventArgs)(arg));
                this.DeleteRegistedAgentCompleted(this, new DeleteRegistedAgentCompletedEventArgs(invokeArgs.Results, invokeArgs.Error, invokeArgs.Cancelled, invokeArgs.UserState));
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
    [System.CodeDom.Compiler.GeneratedCodeAttribute("System.Web.Services", "4.0.30319.18408")]
    public delegate void IsValidClientCompletedEventHandler(object sender, IsValidClientCompletedEventArgs e);
    
    /// <remarks/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("System.Web.Services", "4.0.30319.18408")]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    public partial class IsValidClientCompletedEventArgs : System.ComponentModel.AsyncCompletedEventArgs {
        
        private object[] results;
        
        internal IsValidClientCompletedEventArgs(object[] results, System.Exception exception, bool cancelled, object userState) : 
                base(exception, cancelled, userState) {
            this.results = results;
        }
        
        /// <remarks/>
        public bool Result {
            get {
                this.RaiseExceptionIfNecessary();
                return ((bool)(this.results[0]));
            }
        }
    }
    
    /// <remarks/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("System.Web.Services", "4.0.30319.18408")]
    public delegate void DeleteRegistedAgentCompletedEventHandler(object sender, DeleteRegistedAgentCompletedEventArgs e);
    
    /// <remarks/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("System.Web.Services", "4.0.30319.18408")]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    public partial class DeleteRegistedAgentCompletedEventArgs : System.ComponentModel.AsyncCompletedEventArgs {
        
        private object[] results;
        
        internal DeleteRegistedAgentCompletedEventArgs(object[] results, System.Exception exception, bool cancelled, object userState) : 
                base(exception, cancelled, userState) {
            this.results = results;
        }
        
        /// <remarks/>
        public bool Result {
            get {
                this.RaiseExceptionIfNecessary();
                return ((bool)(this.results[0]));
            }
        }
    }
}

#pragma warning restore 1591