using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace EconGame.Ui;

/// <summary>
/// Runs a calculation on a worker thread and delivers the result on the main thread, dropping stale runs. A new Start cancels the previous run, a
/// fault is reported instead of thrown, and nothing is delivered once the host has been freed. Work must only touch data captured beforehand.
/// </summary>
public sealed class AsyncRun<T>
{
    int _id; CancellationTokenSource? _cts;
    public bool Busy { get; private set; }

    public void Start(Node host, Func<CancellationToken, T> work, Action<T> onDone, Action<Exception>? onFail = null)
    {
        _cts?.Cancel(); _cts = new CancellationTokenSource();
        var ct = _cts.Token; int id = ++_id; Busy = true;
        Task.Run(() => work(ct)).ContinueWith(t =>
            Callable.From(() =>
            {
                if (id != _id) return;
                Busy = false;
                if (!GodotObject.IsInstanceValid(host) || !EconGame.App.Game.Running) return;   // a page that is only out of the tree for now still gets its result
                if (t.IsFaulted) onFail?.Invoke(t.Exception!.GetBaseException());
                else if (t.IsCompletedSuccessfully) onDone(t.Result);
            }).CallDeferred());
    }

    public void Cancel() { _cts?.Cancel(); _id++; Busy = false; }
}
