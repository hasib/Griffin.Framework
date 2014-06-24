﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Griffin.Container;
using Griffin.Logging;

namespace Griffin.ApplicationServices
{
    /// <summary>
    ///     Used to execute all classes that implement <see cref="IBackgroundJob"/>. The jobs are executed in parallell.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This implementation uses your inversion of control container via the interface <see cref="Griffin.Container.IContainer"/>.. A new scope (
    ///         <see
    ///             cref="IContainerScope" />
    ///         ) is created for every time a job is executed.
    ///     </para>
    ///     <para>
    ///         By subscribing on the event <see cref="ScopeClosing"/> you can for instance commit an unit of work everytime a job have been executed.
    ///     </para>
    /// <para>
    /// To be able to run every job in isolation (in an own scope) we need to be able to find all background jobs. To do that a temporary scope
    /// is created during startup to resolve all jobs (<c><![CDATA[scope.ResolveAll<IBackgroundJob>()]]></c>. The jobs are not invoked but only located so that we can map all background job types. Then later
    /// when we are going to execute each job we use <c><![CDATA[scope.Resolve(jobType)]]></c> for every job that is about to be executed.
    /// </para>
    /// </remarks>
    /// <example>
    /// <para>
    /// Example for a windows service class:
    /// </para>
    /// 
    ///     <code>
    /// public class Service1 : ServiceBase
    /// {
    ///     BackgroundJobManager _jobInvoker;
    ///     IContainer  _container;
    /// 
    ///     public Service1()
    ///     {
    ///         _serviceLocator = CreateContainer();
    /// 
    ///         _jobInvoker = new BackgroundJobManager(_container);
    ///         _jobInvoker.ScopeClosing += OnScopeClosing;
    ///     }
    /// 
    ///     public override OnStart(string[] argv)
    ///     {
    ///         _jobInvoker.Start();
    ///     }
    /// 
    ///     public override OnStop()
    ///     {
    ///         _jobInvoker.Stop();
    ///     }
    /// 
    ///     public void CreateContainer()
    ///     {
    ///         // either create your container directly
    ///         // or use the composition root pattern.
    ///     }
    /// 
    ///     // so that we can commit the transaction
    ///     // event will not be invoked if something fails.
    ///     public void OnScopeClosing(object sender, ScopeCreatedEventArgs e)
    ///     {
    ///         <![CDATA[e.Scope.Resolve<IUnitOfWork>().SaveChanges();]]>
    ///     }
    /// }
    /// </code>
    /// </example>
    public class BackgroundJobManager : IDisposable
    {
        private readonly IContainer _container;
        private readonly ILogger _logger = LogManager.GetLogger(typeof(BackgroundJobManager));
        private Timer _timer;
        private List<Type> _jobTypes = new List<Type>();

        /// <summary>
        ///     Initializes a new instance of the <see cref="BackgroundJobManager" /> class.
        /// </summary>
        /// <param name="container">The container.</param>
        public BackgroundJobManager(IContainer container)
        {
            if (container == null) throw new ArgumentNullException("container");
            _container = container;
            _timer = new Timer(OnExecuteJob, null, Timeout.Infinite, Timeout.Infinite);
            StartInterval = TimeSpan.FromSeconds(1);
            ExecuteInterval = TimeSpan.FromSeconds(5);
        }

        /// <summary>
        ///     Amount of time before running the jobs for the first time
        /// </summary>
        public TimeSpan StartInterval { get; set; }

        /// <summary>
        ///     Amount of time that should be passed between every execution run.
        /// </summary>
        public TimeSpan ExecuteInterval { get; set; }

        /// <summary>
        ///     Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            if (_timer == null)
                return;

            _timer.Dispose();
            _timer = null;
        }

        /// <summary>
        ///     A new scope has been created and the jobs are about to be executed.
        /// </summary>
        public event EventHandler<ScopeCreatedEventArgs> ScopeCreated = delegate { };


        /// <summary>
        ///     A job have finished executing.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Will not be invoked if <see cref="JobFailed"/> event sets <c>CanContinue</c> to <c>false</c>.
        /// </para>
        /// </remarks>
        public event EventHandler<ScopeClosingEventArgs> ScopeClosing = delegate { };

        /// <summary>
        ///     One of the jobs failed
        /// </summary>
        /// <remarks>
        ///     Use this event to determine if the rest of the jobs should be able to execute.
        /// </remarks>
        public event EventHandler<BackgroundJobFailedEventArgs> JobFailed = delegate { };

        /// <summary>
        ///     Start executing jobs (once the <see cref="StartInterval"/> have been passed).
        /// </summary>
        /// <remarks>
        /// <para>
        /// 
        /// </para>
        /// </remarks>
        public void Start()
        {
            using (var scope = _container.CreateScope())
            {
                var jobs = scope.ResolveAll<IBackgroundJob>();
                _jobTypes = jobs.Select(x => x.GetType()).ToList();
            }

            _timer.Change(StartInterval, ExecuteInterval);
        }

        /// <summary>
        ///     Stop executing jobs.
        /// </summary>
        public void Stop()
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private void OnExecuteJob(object state)
        {
            // the catch block is outermost as all jobs
            // will share the same transaction

            Parallel.ForEach(_jobTypes, jobType =>
            {
                IBackgroundJob job = null;
                try
                {
                    using (var scope = _container.CreateScope())
                    {
                        try
                        {
                            job = (IBackgroundJob) scope.Resolve(jobType);
                            ScopeCreated(this, new ScopeCreatedEventArgs(scope));
                            job.Execute();
                            ScopeClosing(this, new ScopeClosingEventArgs(scope, true));

                        }
                        catch (Exception exception)
                        {
                            var args = new BackgroundJobFailedEventArgs(job, exception);
                            JobFailed(this, args);
                            ScopeClosing(this, new ScopeClosingEventArgs(scope, false) {Exception = exception});
                        }

                    }
                }
                catch (Exception exception)
                {
                    JobFailed(this, new BackgroundJobFailedEventArgs(new NoJob(exception), exception));
                    _logger.Error("Failed to execute job: " + job, exception);
                }

            });
        }

        private class NoJob : IBackgroundJob
        {
            private readonly Exception _exception;

            public NoJob(Exception exception)
            {
                if (exception == null) throw new ArgumentNullException("exception");
                _exception = exception;
            }

            /// <summary>
            ///     Exekvera jobbet.
            /// </summary>
            /// <remarks>
            ///     Eventuella undantag hanteras av klassen som exekverar jobbet.
            /// </remarks>
            public void Execute()
            {

            }

            /// <summary>
            /// Returns a <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
            /// </summary>
            /// <returns>
            /// A <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
            /// </returns>
            public override string ToString()
            {
                return "NoJob, scope.ResolveAll<IBackgroundJob>() failed. Check exception property for more information. '" +
                       _exception.Message + "'";
            }
        }
    }


}