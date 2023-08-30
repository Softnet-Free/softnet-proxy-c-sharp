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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;

namespace Softnet.Proxy
{
    static class TCPSaeaPool
    {
        public static int POOL_SIZE = 10000;
        public const int MTU = 1500;
        static byte[] UnmanagedBuffer;
        static Queue<SocketAsyncEventArgs> Pool;

        public static void Init()
        {
            Pool = new Queue<SocketAsyncEventArgs>();
            UnmanagedBuffer = new byte[POOL_SIZE * MTU];

            for (int i = 0; i < POOL_SIZE; i++)
            {
                SocketAsyncEventArgs Saea = new SocketAsyncEventArgs();
                int offset = i * MTU;
                Saea.UserToken = offset;
                Saea.SetBuffer(UnmanagedBuffer, offset, MTU);
                Pool.Enqueue(Saea);
            }
        }

        public static SocketAsyncEventArgs Get()
        {
            lock (Pool)
            {
                if (Pool.Count == 0)
                    return null;
                return Pool.Dequeue();
            }
        }

        public static void Add(SocketAsyncEventArgs saea)
        {
            saea.SetBuffer((int)saea.UserToken, MTU);

            lock (Pool)
            {
                Pool.Enqueue(saea);
            }
        }
    }
}
