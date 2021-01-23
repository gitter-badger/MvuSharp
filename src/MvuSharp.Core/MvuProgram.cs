﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace MvuSharp
{
    public class MvuProgram<TComponent, TModel, TMsg, TArgs>
        where TComponent : MvuComponent<TModel, TMsg, TArgs>, new()
        where TModel : class
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IMvuViewEngine<TModel, TMsg, TArgs> _viewEngine;
        private static readonly MvuComponent<TModel, TMsg, TArgs> Component = new TComponent();
        
        private volatile TModel _oldModel;
        private volatile TModel _model;

        public MvuProgram(
            IServiceProvider serviceProvider, 
            IMvuViewEngine<TModel, TMsg, TArgs> viewEngine)
        {
            _serviceProvider = serviceProvider;
            _viewEngine = viewEngine;
        }

        private IMediator CreateMediator()
        {
            var scope = _serviceProvider.CreateScope();
            return new Mediator(scope.ServiceProvider.GetService);
        }

        public async Task InitAsync()
        {
            var (model, cmd) = Component.Init(_viewEngine.GetInitArgs());
            _model = _oldModel = model;
            if (cmd != null)
            {
                var mediator = CreateMediator();
                var msgQueue = new Queue<TMsg>();
                await cmd(mediator, msgQueue.Enqueue, default);
                await MsgLoopAsync(msgQueue, default, mediator);
            }
            else if (_viewEngine != null)
            {
                await _viewEngine.RenderViewAsync(model);
            }
        }

        public async Task DispatchAsync(TMsg msg, CancellationToken cancellationToken = default)
        {
            var msgQueue = new Queue<TMsg>();
            msgQueue.Enqueue(msg);
            await MsgLoopAsync(msgQueue, cancellationToken);
        }

        public bool ModelHasChanged()
        {
            if (ReferenceEquals(_model, _oldModel)) return false;
            _oldModel = _model;
            return true;
        }

        private async Task MsgLoopAsync(Queue<TMsg> msgQueue, CancellationToken cancellationToken, IMediator mediator = null)
        {
            DispatchHandler<TMsg> dispatchHandler = msgQueue.Enqueue;
            while (msgQueue.Count != 0 && !cancellationToken.IsCancellationRequested)
            {
                var (model, cmd) = Component.Update(_model, msgQueue.Dequeue());
                _model = model;

                if (_viewEngine != null)
                {
                    await _viewEngine.RenderViewAsync(model);
                }

                if (cmd == null) continue;
                mediator ??= CreateMediator();
                try
                {
                    await cmd(mediator, dispatchHandler, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}