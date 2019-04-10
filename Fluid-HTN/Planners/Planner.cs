﻿
using System;
using System.Collections.Generic;
using System.Diagnostics;
using FluidHTN.Compounds;
using FluidHTN.PrimitiveTasks;

namespace FluidHTN
{
	public class Planner<T> where T : IContext
	{
        private Queue<ITask> _plan = new Queue<ITask>();
        private ITask _currentTask;

        public void TickPlan( Domain<T> domain, T ctx )
        {
            // Check whether state has changed or the current plan has finished running.
            // and if so, try to find a new plan.
            if ((_currentTask == null && (_plan == null || _plan.Count == 0)) || ctx.IsDirty)
            {
                var partialPlanTemp = ctx.PlanStartTaskParent;
                var partialPlanIndexTemp = ctx.PlanStartTaskChildIndex;

                bool worldStateDirtyReplan = ctx.IsDirty;
                ctx.IsDirty = false;

                if (worldStateDirtyReplan)
                {
                    // If we're simply re-evaluating whether to replace the current plan because
                    // some world state got dirt, then we do not intend to continue a partial plan
                    // right now, but rather see whether the world state changed to a degree where
                    // we should pursue a better plan. Thus, if this replan fails to find a better
                    // plan, we have to add back the partial plan temps cached above.
                    ctx.PlanStartTaskParent = null;
                    ctx.PlanStartTaskChildIndex = 0;

                    // We also need to ensure that the last mtr is up to date with the on-going MTR of the partial plan,
                    // so that any new potential plan that is decomposing from the domain root has to beat the currently
                    // running partial plan.
                    if (partialPlanTemp != null)
                    {
                        ctx.LastMTR.Clear();
                        foreach (var record in ctx.MethodTraversalRecord)
                        {
                            ctx.LastMTR.Add(record);

                        }

                        ctx.LastMTRDebug.Clear();
                        foreach (var record in ctx.MTRDebug)
                        {
                            ctx.LastMTRDebug.Add(record);
                        }
                    }
                }

                var newPlan = domain.FindPlan(ctx);
                if (newPlan != null)
                {
                    _plan.Clear();
                    while (newPlan.Count > 0)
                    {
                        _plan.Enqueue(newPlan.Dequeue());
                    }

                    if (_currentTask != null && _currentTask is IPrimitiveTask t)
                    {
                        t.Stop(ctx);
                        _currentTask = null;
                    }

                    // Copy the MTR into our LastMTR to represent the current plan's decomposition record
                    // that must be beat to replace the plan.
                    if (ctx.MethodTraversalRecord != null)
                    {
                        ctx.LastMTR.Clear();
                        foreach (var record in ctx.MethodTraversalRecord)
                        {
                            ctx.LastMTR.Add(record);
                        }

                        ctx.LastMTRDebug.Clear();
                        foreach (var record in ctx.MTRDebug)
                        {
                            ctx.LastMTRDebug.Add(record);
                        }
                    }
                }
                else if (partialPlanTemp != null)
                {
                    ctx.PlanStartTaskParent = partialPlanTemp;
                    ctx.PlanStartTaskChildIndex = partialPlanIndexTemp;

                    if (ctx.LastMTR.Count > 0)
                    {
                        ctx.MethodTraversalRecord.Clear();
                        foreach (var record in ctx.LastMTR)
                        {
                            ctx.MethodTraversalRecord.Add(record);
                        }
                        ctx.LastMTR.Clear();

                        ctx.MTRDebug.Clear();
                        foreach (var record in ctx.LastMTRDebug)
                        {
                            ctx.MTRDebug.Add(record);
                        }
                        ctx.LastMTRDebug.Clear();
                    }
                }
            }

            if (_currentTask == null && _plan != null && _plan.Count > 0)
            {
                _currentTask = _plan?.Dequeue();
                if (_currentTask != null)
                {
                    foreach (var condition in _currentTask.Conditions)
                    {
                        // If a condition failed, then the plan failed to progress! A replan is required.
                        if (condition.IsValid(ctx) == false)
                        {
                            _currentTask = null;
                            _plan.Clear();

                            ctx.LastMTR.Clear();
                            ctx.LastMTRDebug.Clear();

                            return;
                        }
                    }
                }
            }

            if (_currentTask != null)
            {
                if (_currentTask is IPrimitiveTask task)
                {
                    if (task.Operator != null)
                    {
                        var status = task.Operator.Update(ctx);

                        // If the operation finished successfully, we set task to null so that we dequeue the next task in the plan the following tick.
                        if (status == TaskStatus.Success)
                        {
                            // All effects that is a result of running this task should be applied when the task is a success.
                            foreach (var effect in task.Effects)
                            {
                                if (effect.Type == EffectType.PlanAndExecute)
                                {
                                    effect.Apply(ctx);
                                }
                            }

                            _currentTask = null;
                            if (_plan.Count == 0)
                            {
                                ctx.LastMTR.Clear();
                                ctx.LastMTRDebug.Clear();

                                ctx.IsDirty = false;

                                TickPlan(domain, ctx);
                            }
                        }

                        // If the operation failed to finish, we need to fail the entire plan, so that we will replan the next tick.
                        else if (status == TaskStatus.Failure)
                        {
                            _currentTask = null;
                            _plan.Clear();

                            ctx.LastMTR.Clear();
                            ctx.LastMTRDebug.Clear();
                        }

                        // Otherwise the operation isn't done yet and need to continue.
                    }
                    else
                    {
                        // This should not really happen if a domain is set up properly.
                        _currentTask = null;
                    }
                }
            }
        }

		
	}
}