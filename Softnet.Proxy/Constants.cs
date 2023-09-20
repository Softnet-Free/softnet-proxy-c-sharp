/*
*   Copyright 2023 Robert Koifman
*   
*   Licensed under the Apache License, Version 2.0 (the "License");
*   you may not use this file except in compliance with the License.
*   You may obtain a copy of the License at
*
*   http://www.apache.org/licenses/LICENSE-2.0
*
*   Unless required by applicable law or agreed to in writing, software
*   distributed under the License is distributed on an "AS IS" BASIS,
*   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
*   See the License for the specific language governing permissions and
*   limitations under the License.
*/

using System;

namespace Softnet.Proxy
{
    static class Constants
    {
        public const int TcpRzvPort = 7778;
        public const int UdpRzvPort = 7779;
        public const int BasicProxyPort = 37780;
        public const int max_tcp_params_items = 100000;
        public const int max_tcp_v6_proxy_items = 100000;
        public const int max_tcp_v4_proxy_items = 100000;
        public const long TcpControlTimeoutMilliseconds = 90000;
        public const long ProxySessionTimeoutSeconds = 720;
        public const long ProxyHandshakeTimeoutSeconds = 40;
        public const long TcpProxySessionRenewSeconds = 120;
        public const long UdpProxySessionTimeoutSeconds = 120;

        public static class TcpConnector
        {
            // Input
            public const byte CLIENT_P2P = 1;
            public const byte SERVICE_P2P = 2;
            public const byte CLIENT_PROXY = 3;
            public const byte SERVICE_PROXY = 4;
            public const byte AUTH_HASH = 5;
            public const byte P2P_FAILED = 6;
            // Output
            public const byte AUTH_KEY = 11;
            public const byte CREATE_P2P_CONNECTION = 12;
            public const byte CREATE_P2P_CONNECTION_IN_DUAL_MODE = 13;
            public const byte CREATE_PROXY_CONNECTION = 14;
            public const byte ERROR = 15;
        }

        public static class TcpProxy
        {
            // Input/Output
            public const byte CLIENT_PROXY_ENDPOINT = 1;
            public const byte SERVICE_PROXY_ENDPOINT = 2;
        }

        public static class UdpConnector
        {
            // Client Output
        	public const byte CLIENT_ENDPOINT = 1;
        	// Service Output
        	public const byte SERVICE_ENDPOINT = 2;
        	// Input
        	public const byte AUTH_HASH = 3;
        	// Service Output
        	public const byte CREATE_PROXY_CONNECTION = 4;        	            
        	
        	// Output
            public const byte ERROR = 10;
        	public const byte AUTH_KEY = 11;
        	public const byte CREATE_P2P_CONNECTION = 12;
        	public const byte CREATE_P2P_CONNECTION_IN_DUAL_MODE = 13;
        	public const byte PROXY_CONNECTION_CREATED = 14;
        	
        	// Client Input / Service Output
        	public const byte P2P_HOLE_PUNCHED = 21;        	            
        	public const byte P2P_LOCAL_HOLE_PUNCHED = 22;        	            
        	// Client Output / Service Input
            public const byte P2P_CONNECTION_CREATED = 21;        	            
        }

        public static class UdpEndpoint        
        {
        	// Input
            public const byte ENDPOINT_INFO = 86;
        }
    }
}
