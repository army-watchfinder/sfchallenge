﻿using System.Collections.Generic;
using System.Fabric;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using Microsoft.ServiceFabric.Services.Remoting.Runtime;
using UserStore.Interface;
using Common;
using Microsoft.ServiceFabric.Data.Collections;
using System;
using System.Threading;
using Microsoft.ServiceFabric.Data;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.ServiceFabric;

namespace UserStore
{
    /// <summary>
    /// A stateful service, used to store Users. Access via binary remoting based on the V2 Remoting stack
    /// </summary>
    public sealed class UserStore : StatefulService, IUserStore
    {
        public const string StateManagerKey = "UserStore";
        private Metrics MetricsLog;

        public UserStore(StatefulServiceContext context)
            : base(context)
        {
            Init();
        }
        public UserStore(StatefulServiceContext context, IReliableStateManagerReplica2 reliableStateManagerReplica)
            : base(context, reliableStateManagerReplica)
        {
        }

        private void Init()
        {
            // Get configuration from our PackageRoot/Config/Setting.xml file
            var configurationPackage = Context.CodePackageActivationContext.GetConfigurationPackageObject("Config");

            // Metrics used to compare team performance and reliability against each other
            var metricsInstrumentationKey = configurationPackage.Settings.Sections["UserStoreConfig"].Parameters["Admin_AppInsights_InstrumentationKey"].Value;
            var teamName = configurationPackage.Settings.Sections["UserStoreConfig"].Parameters["TeamName"].Value;
            this.MetricsLog = new Metrics(metricsInstrumentationKey, teamName);
        }

        /// <summary>
        /// Standard implementation for service endpoints using the V2 Remoting stack. See more: https://aka.ms/servicefabricservicecommunication
        /// </summary>
        /// <remarks>
        /// Iterates over the ServiceManifest.xml and expects a endpoint with name ServiceEndpointV2 to expose the service over Remoting.
        /// </remarks>
        /// <returns>A collection of listeners.</returns>
        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            return this.CreateServiceRemotingReplicaListeners();
        }

        public async Task<User> GetUserAsync(string userId, CancellationToken cancellationToken)
        {
            IReliableDictionary<string, User> users =
               await this.StateManager.GetOrAddAsync<IReliableDictionary<string, User>>(StateManagerKey);

            var executed = false;
            var retryCount = 0;
            List<Exception> exceptions = new List<Exception>();
            while (!executed && retryCount < 3)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    return await ExecuteGetUserAsync(userId, users, cancellationToken);
                }
                catch (TimeoutException ex)
                {
                    exceptions.Add(ex);
                    retryCount++;
                    continue;
                }
                catch (TransactionFaultedException ex)
                {
                    exceptions.Add(ex);
                    retryCount++;
                    continue;
                }
            }
            if (exceptions.Count > 0)
                throw new AggregateException(
                    "Encounted errors while trying to get user",
                    exceptions);
            return null; // no-op
            
        }

        public async Task<List<User>> GetUsersAsync(CancellationToken cancellationToken)
        {
            IReliableDictionary<string, User> users =
               await this.StateManager.GetOrAddAsync<IReliableDictionary<string, User>>(StateManagerKey);

            var returnList = new List<User>();

            using (var tx = this.StateManager.CreateTransaction())
            {
                var asyncEnumerable = await users.CreateEnumerableAsync(tx);
                var asyncEnumerator = asyncEnumerable.GetAsyncEnumerator();

                try
                {
                    while (await asyncEnumerator.MoveNextAsync(cancellationToken))
                    {
                        returnList.Add(asyncEnumerator.Current.Value);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Ignore
                }

                await tx.CommitAsync();
            }
            return returnList;
        }

        public async Task<string> AddUserAsync(User user, CancellationToken cancellationToken)
        {
            IReliableDictionary<string, User> users =
              await this.StateManager.GetOrAddAsync<IReliableDictionary<string, User>>(StateManagerKey);

            var executed = false;
            var retryCount = 0;
            List<Exception> exceptions = new List<Exception>();
            while (!executed && retryCount < 3)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var userId = await ExecuteAddUserAsync(user, users, cancellationToken);
                    executed = true;
                    return userId;
                }
                catch (TimeoutException ex)
                {
                    exceptions.Add(ex);
                    retryCount++;
                    continue;
                }
                catch (TransactionFaultedException ex)
                {
                    exceptions.Add(ex);
                    retryCount++;
                    continue;
                }
            }
            if (exceptions.Count > 0)
                throw new AggregateException(
                    "Encounted errors while trying to add user",
                    exceptions);
            return string.Empty; // no-op
        }

        public async Task<bool> UpdateUsersAsync(List<User> userList, CancellationToken cancellationToken)
        {
            IReliableDictionary<string, User> users =
              await this.StateManager.GetOrAddAsync<IReliableDictionary<string, User>>(StateManagerKey);

            var executed = false;
            var retryCount = 0;
            List<Exception> exceptions = new List<Exception>();
            while (!executed && retryCount < 3)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using (var tx = this.StateManager.CreateTransaction())
                    {
                        foreach (var user in userList)
                        {
                            await ExecuteUpdateUserTransactionalAsync(tx, user, users, cancellationToken);

                            MetricsLog?.UserUpdated(user);
                        }
                        await tx.CommitAsync();
                    }
                    executed = true;
                }
                catch (TimeoutException ex)
                {
                    exceptions.Add(ex);
                    retryCount++;
                    continue;
                }
                catch (TransactionFaultedException ex)
                {
                    exceptions.Add(ex);
                    retryCount++;
                    continue;
                }
            }
            if (exceptions.Count > 0)
                throw new AggregateException(
                    "Encounted errors while trying to add user",
                    exceptions);
            return executed; // no-op
        }

        public async Task<bool> UpdateUserAsync(User user, CancellationToken cancellationToken)
        {
            IReliableDictionary<string, User> users =
              await this.StateManager.GetOrAddAsync<IReliableDictionary<string, User>>(StateManagerKey);

            var executed = false;
            var retryCount = 0;
            List<Exception> exceptions = new List<Exception>();
            while (!executed && retryCount < 3)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var userId = await ExecuteUpdateUserAsync(user, users, cancellationToken);
                    executed = true;
                    return userId;
                }
                catch (TimeoutException ex)
                {
                    exceptions.Add(ex);
                    retryCount++;
                    continue;
                }
                catch (TransactionFaultedException ex)
                {
                    exceptions.Add(ex);
                    retryCount++;
                    continue;
                }
            }
            if (exceptions.Count > 0)
                throw new AggregateException(
                    "Encounted errors while trying to update user",
                    exceptions);
            return false; // no-op
        }

        public async Task<bool> DeleteUserAsync(string userId, CancellationToken cancellationToken)
        {
            IReliableDictionary<string, User> users =
              await this.StateManager.GetOrAddAsync<IReliableDictionary<string, User>>(StateManagerKey);

            var executed = false;
            var retryCount = 0;
            List<Exception> exceptions = new List<Exception>();
            while (!executed && retryCount < 3)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var deleted = await ExecuteDeleteUserAsync(userId, users, cancellationToken);
                    executed = true;
                    return deleted;
                }
                catch (TimeoutException ex)
                {
                    exceptions.Add(ex);
                    retryCount++;
                    continue;
                }
                catch (TransactionFaultedException ex)
                {
                    exceptions.Add(ex);
                    retryCount++;
                    continue;
                }
            }
            if (exceptions.Count > 0)
                throw new AggregateException(
                    "Encounted errors while trying to add user",
                    exceptions);
            return false; // no-op
        }

        private async Task<string> ExecuteAddUserAsync(User user, IReliableDictionary<string, User> users, CancellationToken cancellationToken)
        {
            using (var tx = this.StateManager.CreateTransaction())
            {
                var current = await users.TryGetValueAsync(tx, user.Id);
                if (current.HasValue)
                {
                    return user.Id; // Return existing user
                }
                await users.AddAsync(tx, user.Id, user, TimeSpan.FromSeconds(15), cancellationToken);
                await tx.CommitAsync();

                MetricsLog?.UserCreated(user);
            }
            return user.Id;
        }

        private async Task<bool> ExecuteUpdateUserAsync(User user, IReliableDictionary<string, User> users, CancellationToken cancellationToken)
        {
            bool result;
            using (var tx = this.StateManager.CreateTransaction())
            {
                var current = await users.TryGetValueAsync(tx, user.Id, LockMode.Update);
                if (current.HasValue)
                {
                    result = await users.TryUpdateAsync(tx, user.Id, user, current.Value, TimeSpan.FromSeconds(15), cancellationToken);
                    await tx.CommitAsync();

                    MetricsLog?.UserUpdated(user);
                }
                else
                {
                    throw new ApplicationException($"Cannot update non existent user '{user.Id}'");
                }
            }
            return result;
        }

        private async Task ExecuteUpdateUserTransactionalAsync(ITransaction tx, User user, IReliableDictionary<string, User> users, CancellationToken cancellationToken)
        {
            var current = await users.TryGetValueAsync(tx, user.Id, LockMode.Update);
            if (current.HasValue)
            {
                await users.TryUpdateAsync(tx, user.Id, user, current.Value, TimeSpan.FromSeconds(15), cancellationToken);
            }
            else
            {
                throw new ApplicationException($"Cannot update non existent user '{user.Id}'");
            }
        }

        private async Task<User> ExecuteGetUserAsync(string userId, IReliableDictionary<string, User> users, CancellationToken cancellationToken)
        {
            User user = null;
            using (var tx = this.StateManager.CreateTransaction())
            {
                var tryUser = await users.TryGetValueAsync(tx, userId, LockMode.Default, TimeSpan.FromSeconds(15), cancellationToken);
                if (tryUser.HasValue)
                {
                    user = tryUser.Value;
                }
                await tx.CommitAsync();
            }
            return user;
        }

        private async Task<bool> ExecuteDeleteUserAsync(string userId, IReliableDictionary<string, User> users, CancellationToken cancellationToken)
        {
            bool removed;
            using (var tx = this.StateManager.CreateTransaction())
            {
                var result = await users.TryRemoveAsync(tx, userId, TimeSpan.FromSeconds(15), cancellationToken);
                if (result.HasValue)
                {
                    removed = true;
                }
                else
                {
                    removed = false;
                }
                await tx.CommitAsync();
            }
            return removed;
        }
    }
}