﻿using System;
using System.Linq;
using System.Collections.Generic;
using ByteartRetail.Infrastructure;
using System.Threading;
using System.Transactions;
using ByteartRetail.Domain.Events;
using ByteartRetail.Events;

namespace ByteartRetail.Domain.Repositories
{
    /// <summary>
    /// Represents the base class for repository contexts.
    /// </summary>
    public abstract class RepositoryContext : DisposableObject, IRepositoryContext
    {
        #region Private Fields
        private readonly Guid id = Guid.NewGuid();
        private readonly ThreadLocal<Dictionary<Guid, object>> localNewCollection = new ThreadLocal<Dictionary<Guid, object>>(() => new Dictionary<Guid, object>());
        private readonly ThreadLocal<Dictionary<Guid, object>> localModifiedCollection = new ThreadLocal<Dictionary<Guid, object>>(() => new Dictionary<Guid, object>());
        private readonly ThreadLocal<Dictionary<Guid, object>> localDeletedCollection = new ThreadLocal<Dictionary<Guid, object>>(() => new Dictionary<Guid, object>());
        private readonly ThreadLocal<bool> localCommitted = new ThreadLocal<bool>(() => true);
        private readonly ThreadLocal<Dictionary<IAggregateRoot, List<IEvent>>> pendingEvents = new ThreadLocal<Dictionary<IAggregateRoot, List<IEvent>>>(() => new Dictionary<IAggregateRoot, List<IEvent>>());
        private readonly object sync = new object();
        #endregion

        #region Ctor
        public RepositoryContext()
        {
        }
        #endregion

        #region Protected Methods
        /// <summary>
        /// Clears all the registration in the repository context.
        /// </summary>
        /// <remarks>Note that this can only be called after the repository context has successfully committed.</remarks>
        protected void ClearRegistrations()
        {
            this.localNewCollection.Value.Clear();
            this.localModifiedCollection.Value.Clear();
            this.localDeletedCollection.Value.Clear();
        }
        /// <summary>
        /// Disposes the object.
        /// </summary>
        /// <param name="disposing">A <see cref="System.Boolean"/> value which indicates whether
        /// the object should be disposed explicitly.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.localCommitted.Dispose();
                this.localDeletedCollection.Dispose();
                this.localModifiedCollection.Dispose();
                this.localNewCollection.Dispose();
            }
        }

        protected abstract void DoCommit();
        #endregion

        #region Protected Properties
        /// <summary>
        /// Gets an enumerator which iterates over the collection that contains all the objects need to be added to the repository.
        /// </summary>
        protected IEnumerable<KeyValuePair<Guid, object>> NewCollection
        {
            get { return localNewCollection.Value; }
        }
        /// <summary>
        /// Gets an enumerator which iterates over the collection that contains all the objects need to be modified in the repository.
        /// </summary>
        protected IEnumerable<KeyValuePair<Guid, object>> ModifiedCollection
        {
            get { return localModifiedCollection.Value; }
        }
        /// <summary>
        /// Gets an enumerator which iterates over the collection that contains all the objects need to be deleted from the repository.
        /// </summary>
        protected IEnumerable<KeyValuePair<Guid, object>> DeletedCollection
        {
            get { return localDeletedCollection.Value; }
        }
        #endregion

        #region IRepositoryContext Members
        /// <summary>
        /// Gets the ID of the repository context.
        /// </summary>
        public Guid ID
        {
            get { return id; }
        }
        /// <summary>
        /// Registers a new object to the repository context.
        /// </summary>
        /// <typeparam name="TAggregateRoot">The type of the aggregate root.</typeparam>
        /// <param name="obj">The object to be registered.</param>
        public virtual void RegisterNew<TAggregateRoot>(TAggregateRoot obj) where TAggregateRoot : class, IAggregateRoot
        {
            if (obj.ID.Equals(Guid.Empty))
                throw new ArgumentException("The ID of the object is empty.", "obj");
            //if (modifiedCollection.ContainsKey(obj.ID))
            if (localModifiedCollection.Value.ContainsKey(obj.ID))
                throw new InvalidOperationException("The object cannot be registered as a new object since it was marked as modified.");
            if (localNewCollection.Value.ContainsKey(obj.ID))
                throw new InvalidOperationException("The object has already been registered as a new object.");
            localNewCollection.Value.Add(obj.ID, obj);
            localCommitted.Value = false;
        }
        /// <summary>
        /// Registers a modified object to the repository context.
        /// </summary>
        /// <typeparam name="TAggregateRoot">The type of the aggregate root.</typeparam>
        /// <param name="obj">The object to be registered.</param>
        public virtual void RegisterModified<TAggregateRoot>(TAggregateRoot obj) where TAggregateRoot : class, IAggregateRoot
        {
            if (obj.ID.Equals(Guid.Empty))
                throw new ArgumentException("The ID of the object is empty.", "obj");
            if (localDeletedCollection.Value.ContainsKey(obj.ID))
                throw new InvalidOperationException("The object cannot be registered as a modified object since it was marked as deleted.");
            if (!localModifiedCollection.Value.ContainsKey(obj.ID) && !localNewCollection.Value.ContainsKey(obj.ID))
                localModifiedCollection.Value.Add(obj.ID, obj);
            localCommitted.Value = false;
        }
        /// <summary>
        /// Registers a deleted object to the repository context.
        /// </summary>
        /// <typeparam name="TAggregateRoot">The type of the aggregate root.</typeparam>
        /// <param name="obj">The object to be registered.</param>
        public virtual void RegisterDeleted<TAggregateRoot>(TAggregateRoot obj) where TAggregateRoot : class, IAggregateRoot
        {
            if (obj.ID.Equals(Guid.Empty))
                throw new ArgumentException("The ID of the object is empty.", "obj");
            if (localNewCollection.Value.ContainsKey(obj.ID))
            {
                if (localNewCollection.Value.Remove(obj.ID))
                    return;
            }
            bool removedFromModified = localModifiedCollection.Value.Remove(obj.ID);
            bool addedToDeleted = false;
            if (!localDeletedCollection.Value.ContainsKey(obj.ID))
            {
                localDeletedCollection.Value.Add(obj.ID, obj);
                addedToDeleted = true;
            }
            localCommitted.Value = !(removedFromModified || addedToDeleted);
        }

        #endregion

        #region IUnitOfWork Members
        /// <summary>
        /// 获得一个<see cref="System.Boolean"/>值，该值表示当前的Unit Of Work是否支持Microsoft分布式事务处理机制。
        /// </summary>
        public abstract bool DistributedTransactionSupported { get; }
        /// <summary>
        /// Gets a <see cref="System.Boolean"/> value which indicates whether the UnitOfWork
        /// was committed.
        /// </summary>
        public bool Committed
        {
            get { return localCommitted.Value; }
            protected set { localCommitted.Value = value; }
        }
        /// <summary>
        /// Commits the UnitOfWork.
        /// </summary>
        public virtual void Commit()
        {
            this.DoCommit();
        }
        /// <summary>
        /// Rolls-back the UnitOfWork.
        /// </summary>
        public abstract void Rollback();

        #endregion

    }
}
