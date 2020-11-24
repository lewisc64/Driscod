using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Driscod
{
    public static class TaskExtensions
    {
        public static Task Forget(this Task task)
        {
            return task;
        }
    }
}
