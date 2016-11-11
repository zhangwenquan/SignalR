// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using System.Collections.Concurrent;

namespace ChatSample.Hubs
{
    // TODO: Make this work
    [Authorize]
    public class Chat : Hub
    {
        private static ConcurrentDictionary<string, HashSet<string>> _userRooms = new ConcurrentDictionary<string, HashSet<string>>();

        public override async Task OnConnectedAsync()
        {
            if (!Context.User.Identity.IsAuthenticated)
            {
                Context.Connection.Channel.Dispose();
            }
            else
            {
                await Clients.All.InvokeAsync("UpdateUser", Context.User.Identity.Name, "online");
            }
        }

        public override Task OnDisconnectedAsync()
        {
            // TODO: Leave rooms

            if (!string.IsNullOrEmpty(Context.User.Identity.Name))
            {
                Clients.All.InvokeAsync("UpdateUser", Context.User.Identity.Name, "offline");
            }

            return Task.CompletedTask;
        }

        public async Task Send(string message)
        {
            message = message.Trim();

            if (message.StartsWith("/"))
            {
                if (message.StartsWith("/join"))
                {
                    var tokens = message.Split(' ');

                    if (tokens.Length >= 2)
                    {
                        var room = tokens[1];

                        // TODO: track rooms

                        //_userRooms.AddOrUpdate(room, r => new HashSet<string> { Context.User.Identity.Name }, (r, set) => { set.Add(Context.User.Identity.Name); return set; });
                        await Clients.Group(room).InvokeAsync("Send", room, $"{Context.User.Identity.Name} joined {room}");
                        await Groups.AddAsync(room);
                        await Clients.Client(Context.ConnectionId).InvokeAsync("JoinRoom", room);
                    }
                }
                else if (message.StartsWith("/leave"))
                {
                    var tokens = message.Split(' ');

                    if (tokens.Length >= 2)
                    {
                        var room = tokens[1];

                        // TODO: track rooms
                        await Groups.RemoveAsync(room);
                        await Clients.Group(room).InvokeAsync("Send", room, $"{Context.User.Identity.Name} left {room}");
                    }
                }
            }
            else
            {
                await Clients.All.InvokeAsync("Send", "All", $"{Context.User.Identity.Name}: {message}");
            }
        }
    }
}
