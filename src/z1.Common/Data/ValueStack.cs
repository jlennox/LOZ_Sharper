using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace z1.Common.Data;

public ref struct ValueStack<T>
{
    internal const int MaxInline = 10;

    private T _v0, _v1, _v2, _v3, _v4, _v5, _v6, _v7, _v8, _v9;
    private int _count;
    private Stack<T>? _overflow;

    public int Count => _overflow?.Count ?? _count;

    public ValueStack(T value)
    {
        _v0 = value;
        _count = 1;
        _overflow = null;
    }

    public void Push(T value)
    {
        if (_overflow != null)
        {
            _overflow.Push(value);
            return;
        }

        switch (_count)
        {
            case 0: _v0 = value; break;
            case 1: _v1 = value; break;
            case 2: _v2 = value; break;
            case 3: _v3 = value; break;
            case 4: _v4 = value; break;
            case 5: _v5 = value; break;
            case 6: _v6 = value; break;
            case 7: _v7 = value; break;
            case 8: _v8 = value; break;
            case 9: _v9 = value; break;
            default:
                _overflow = new Stack<T>(MaxInline);
                _overflow.Push(_v0);
                _overflow.Push(_v1);
                _overflow.Push(_v2);
                _overflow.Push(_v3);
                _overflow.Push(_v4);
                _overflow.Push(_v5);
                _overflow.Push(_v6);
                _overflow.Push(_v7);
                _overflow.Push(_v8);
                _overflow.Push(_v9);
                _overflow.Push(value);
                _count = 0;
                return;
        }

        _count++;
    }

    public T Pop()
    {
        if (_overflow != null) return _overflow.Pop();

        var val = _count switch
        {
            9 => _v9,
            8 => _v8,
            7 => _v7,
            6 => _v6,
            5 => _v5,
            4 => _v4,
            3 => _v3,
            2 => _v2,
            1 => _v1,
            0 => _v0,
            _ => throw new InvalidOperationException("Stack is empty")
        };

        --_count;
        return val;
    }

    public bool TryPop([MaybeNullWhen(false)] out T result)
    {
        if (_overflow != null) return _overflow.TryPop(out result);

        if (_count == 0)
        {
            result = default!;
            return false;
        }

        _count--;
        result = _count switch
        {
            9 => _v9,
            8 => _v8,
            7 => _v7,
            6 => _v6,
            5 => _v5,
            4 => _v4,
            3 => _v3,
            2 => _v2,
            1 => _v1,
            0 => _v0,
            _ => throw new UnreachableCodeException()
        };
        return true;
    }

    public T Peek()
    {
        if (_overflow != null) return _overflow.Peek();
        if (_count == 0) throw new InvalidOperationException("Stack is empty");

        return (_count - 1) switch
        {
            9 => _v9,
            8 => _v8,
            7 => _v7,
            6 => _v6,
            5 => _v5,
            4 => _v4,
            3 => _v3,
            2 => _v2,
            1 => _v1,
            0 => _v0,
            _ => throw new InvalidOperationException("Unreachable")
        };
    }

    // The result is the opposite of the order in which items were pushed.
    // IE, the first item in the result span would be the last popped from the stack.
    public bool TryGetSpan([MaybeNullWhen(false)] out ReadOnlySpan<T> result)
    {
        if (_overflow != null)
        {
            result = default;
            return false;
        }

        result = MemoryMarshal.CreateSpan(ref _v0, _count);
        return true;
    }

    public void Add(in T value) => Push(value);
}