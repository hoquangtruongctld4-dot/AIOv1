using System;
using System.Collections.Generic;
using System.Diagnostics;
using Newtonsoft.Json;

namespace subphimv1.Services
{

    public sealed class UndoRedoService<TState>
        where TState : class
    {
        private readonly Stack<TState> _undoStack = new();
        private readonly Stack<TState> _redoStack = new();
        private readonly int _maxStackSize;
        private bool _isApplyingState;

        public UndoRedoService(int maxStackSize = 50)
        {
            _maxStackSize = Math.Max(1, maxStackSize);
        }

        public bool CanUndo => _undoStack.Count > 1;   
        public bool CanRedo => _redoStack.Count > 0;

        public int UndoCount => _undoStack.Count;
        public int RedoCount => _redoStack.Count;

        /// <summary>
        /// Deep clone via JSON (safe, simple, avoids reference sharing).
        /// </summary>
        private static TState DeepClone(TState state)
        {
            if (state == null) return null;
            var json = JsonConvert.SerializeObject(state, Formatting.None,
                new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                    NullValueHandling = NullValueHandling.Include
                });
            return JsonConvert.DeserializeObject<TState>(json,
                new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                    ObjectCreationHandling = ObjectCreationHandling.Replace
                });
        }
        public void AddState(TState state)
        {
            if (state == null) return;
            if (_isApplyingState) return;

            var clone = DeepClone(state);
            _undoStack.Push(clone);
            while (_undoStack.Count > _maxStackSize)
            {
                var temp = new Stack<TState>(_undoStack);
                _undoStack.Clear();
                int keep = _maxStackSize;
                foreach (var s in temp)
                {
                    if (keep-- <= 0) break;
                    _undoStack.Push(s);
                }
            }
            _redoStack.Clear();

            Debug.WriteLine($"[UndoRedo] AddState: undo={_undoStack.Count}, redo={_redoStack.Count}");
        }
        public TState Undo()
        {
            if (!CanUndo) return null;

            _isApplyingState = true;
            try
            {
                // current => redo
                var current = _undoStack.Pop();
                _redoStack.Push(current);

                // previous => to apply (return deep copy)
                var previous = _undoStack.Peek();
                var clone = DeepClone(previous);

                Debug.WriteLine($"[UndoRedo] Undo: undo={_undoStack.Count}, redo={_redoStack.Count}");
                return clone;
            }
            finally
            {
                _isApplyingState = false;
            }
        }

        /// <summary>
        /// Redo: move top of redo back to undo and return it as current.
        /// </summary>
        public TState Redo()
        {
            if (!CanRedo) return null;

            _isApplyingState = true;
            try
            {
                var redoState = _redoStack.Pop();
                _undoStack.Push(redoState);
                var clone = DeepClone(redoState);

                Debug.WriteLine($"[UndoRedo] Redo: undo={_undoStack.Count}, redo={_redoStack.Count}");
                return clone;
            }
            finally
            {
                _isApplyingState = false;
            }
        }

        /// <summary>
        /// Clear both stacks.
        /// </summary>
        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            Debug.WriteLine("[UndoRedo] Clear stacks");
        }

        /// <summary>
        /// Prime the service with an initial state (becomes 'current').
        /// </summary>
        public void SetInitialState(TState initial)
        {
            Clear();
            if (initial != null)
            {
                _undoStack.Push(DeepClone(initial));
            }
            Debug.WriteLine($"[UndoRedo] SetInitialState: undo={_undoStack.Count}, redo={_redoStack.Count}");
        }
    }
}
