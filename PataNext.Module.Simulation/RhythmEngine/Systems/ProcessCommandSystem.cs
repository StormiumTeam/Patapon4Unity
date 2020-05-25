﻿using System;
using System.Collections.Generic;
using System.Threading;
using Collections.Pooled;
using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using GameHost.Core.Ecs;
using GameHost.Core.Threading;
using PataNext.Module.RhythmEngine.Data;
using PataponGameHost.RhythmEngine.Components;

namespace PataNext.Module.RhythmEngine
{
	public class ProcessCommandSystem : RhythmEngineSystemBase
	{
		private readonly GetNextCommandSystem getNextCommandSystem;
		private readonly ApplyCommandSystem   applyCommandSystem;

		public ProcessCommandSystem(WorldCollection collection) : base(collection)
		{
			getNextCommandSystem = new GetNextCommandSystem(World.Mgr.GetEntities()
			                                                     .With<RhythmEngineIsPlaying>()
			                                                     .With<RhythmEngineSettings>()
			                                                     .With<RhythmEngineLocalState>()
			                                                     .With<RhythmEngineExecutingCommand>()
			                                                     .With<RhythmEngineLocalCommandBuffer>()
			                                                     .With<RhythmEnginePredictedCommandBuffer>()
			                                                     .AsSet());
			applyCommandSystem = new ApplyCommandSystem(World.Mgr.GetEntities()
			                                                 .With<RhythmEngineIsPlaying>()
			                                                 .With<RhythmEngineSettings>()
			                                                 .With<RhythmEngineLocalState>()
			                                                 .With<RhythmEngineExecutingCommand>()
			                                                 .With<GameComboState>()
			                                                 .With<GameCommandState>()
			                                                 .With<RhythmEnginePredictedCommandBuffer>()
			                                                 .AsSet());
		}

		protected override void OnUpdate()
		{
			base.OnUpdate();
			getNextCommandSystem.Update(0);
			applyCommandSystem.Update(0);
		}

		public class GetNextCommandSystem : AEntitySystem<float>
		{
			private EntitySet    commandSet;

			private ThreadLocal<List<Entity>> cmdOutput = new ThreadLocal<List<Entity>>(() => new List<Entity>());

			public GetNextCommandSystem(EntitySet set) : base(set, new DefaultParallelRunner(Processor.GetWorkerCount(1.0)))
			{
				commandSet = set.World.GetEntities()
				                .With<RhythmCommandDefinition>()
				                .AsSet();
			}

			protected override void Update(float _, in Entity entity)
			{
				ref readonly var state = ref entity.Get<RhythmEngineLocalState>();
				if (!state.CanRunCommands)
					return;

				ref readonly var commandProgression = ref entity.Get<RhythmEngineLocalCommandBuffer>();

				ref var executingCommand  = ref entity.Get<RhythmEngineExecutingCommand>();
				ref var predictedCommands = ref entity.Get<RhythmEnginePredictedCommandBuffer>();

				var output = this.cmdOutput.Value;
				output.Clear();
				
				RhythmCommandUtility.GetCommand(commandSet, commandProgression, output, false);

				predictedCommands.Clear();
				predictedCommands.AddRange(output);
				if (predictedCommands.Count == 0)
				{
					RhythmCommandUtility.GetCommand(commandSet, commandProgression, output, true);
					if (output.Count > 0)
					{
						predictedCommands.AddRange(output);
					}

					return;
				}

				// this is so laggy clients don't have a weird things when their command has been on another beat on the server
				var targetBeat = commandProgression[^1].FlowBeat + 1;

				executingCommand.Previous            = executingCommand.CommandTarget;
				executingCommand.CommandTarget       = output[0];
				executingCommand.ActivationBeatStart = targetBeat;
				executingCommand.ActivationBeatEnd   = targetBeat + executingCommand.CommandTarget.Get<RhythmCommandDefinition>().Duration;
				executingCommand.WaitingForApply     = true;

				var power = 0.0f;
				for (var i = 0; i != commandProgression.Count; i++)
				{
					// perfect
					if (commandProgression[i].GetAbsoluteScore() <= FlowPressure.Perfect)
						power += 1.0f;
					else
						power += 0.33f;
				}
				
				executingCommand.Power = power / commandProgression.Count;
				commandProgression.Clear();
			}

			public override void Dispose()
			{
				base.Dispose();
				cmdOutput.Dispose();
			}
		}

		public class ApplyCommandSystem : AEntitySystem<float>
		{
			public ApplyCommandSystem(EntitySet set) : base(set, new DefaultParallelRunner(Processor.GetWorkerCount(0.5)))
			{

			}

			protected override void Update(float _, in Entity entity)
			{
				ref readonly var settings     = ref entity.Get<RhythmEngineSettings>();
				ref var          state        = ref entity.Get<RhythmEngineLocalState>();
				ref var          commandState = ref entity.Get<GameCommandState>();
				if (!state.CanRunCommands)
				{
					commandState.Reset();
					return;
				}

				// TODO: Apply Ability Selection

				const int mercy    = 1; // increase it by one on a server
				const int cmdMercy = 0; // increase it by three on a server

				ref var executing  = ref entity.Get<RhythmEngineExecutingCommand>();
				ref var comboState = ref entity.Get<GameComboState>();

				var rhythmActiveAtFlowBeat = executing.ActivationBeatStart;

				var checkStopBeat = Math.Max(state.LastPressure.FlowBeat + mercy,
					RhythmEngineUtility.GetFlowBeat(new TimeSpan(commandState.EndTimeMs * TimeSpan.TicksPerMillisecond), settings.BeatInterval) + cmdMercy);
				if (true) // todo: !isServer && simulateTagFromEntity.Exists(entity)
				{
					checkStopBeat = Math.Max(checkStopBeat,
						RhythmEngineUtility.GetFlowBeat(new TimeSpan(commandState.EndTimeMs * TimeSpan.TicksPerMillisecond), settings.BeatInterval));
				}
				
				var flowBeat       = RhythmEngineUtility.GetFlowBeat(state, settings);
				var activationBeat = RhythmEngineUtility.GetActivationBeat(state, settings);
				if (state.IsRecovery(flowBeat)
				    || (rhythmActiveAtFlowBeat < flowBeat && checkStopBeat < activationBeat)
				    || (executing.CommandTarget == default && entity.Get<RhythmEnginePredictedCommandBuffer>().Count != 0 && rhythmActiveAtFlowBeat < state.LastPressure.FlowBeat)
				    || (entity.Get<RhythmEnginePredictedCommandBuffer>().Count == 0))
				{
					commandState.Reset();
				}

				if (executing.CommandTarget == default || state.IsRecovery(flowBeat))
				{
					commandState.Reset();
					return;
				}

				if (!executing.WaitingForApply)
					return;
				executing.WaitingForApply = false;

				Console.WriteLine("command applied!");

				var beatLength = executing.CommandTarget.Get<RhythmCommandDefinition>().Duration;

				// if (!isServer && settings.UseClientSimulation && simulateTagFromEntity.Exists(entity))
				if (true)
				{
					commandState.ChainEndTimeMs = (int) ((rhythmActiveAtFlowBeat + beatLength + 4) * settings.BeatInterval.Ticks / TimeSpan.TicksPerMillisecond);

					//comboState.Update(executing, true);

					commandState.StartTimeMs = (int) (executing.ActivationBeatStart * settings.BeatInterval.Ticks / TimeSpan.TicksPerMillisecond);
					commandState.EndTimeMs   = (int) (executing.ActivationBeatEnd * settings.BeatInterval.Ticks / TimeSpan.TicksPerMillisecond);
				}
			}
		}
	}
}