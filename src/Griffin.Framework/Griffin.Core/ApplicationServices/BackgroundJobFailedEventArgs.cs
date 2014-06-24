﻿using System;

namespace Griffin.ApplicationServices
{
    /// <summary>
    ///     Failed to execute a job. 
    /// </summary>
    public class BackgroundJobFailedEventArgs : EventArgs
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="BackgroundJobFailedEventArgs" /> class.
        /// </summary>
        /// <param name="job">Job that failed</param>
        /// <param name="exception">Exception that the job threw.</param>
        public BackgroundJobFailedEventArgs(IBackgroundJob job, Exception exception)
        {
            if (job == null) throw new ArgumentNullException("job");
            if (exception == null) throw new ArgumentNullException("exception");

            Job = job;
            Exception = exception;
        }

        /// <summary>
        ///     Job that failed.
        /// </summary>
        public IBackgroundJob Job { get; private set; }

        /// <summary>
        ///     Exception that the job threw.
        /// </summary>
        public Exception Exception { get; private set; }
    }
}