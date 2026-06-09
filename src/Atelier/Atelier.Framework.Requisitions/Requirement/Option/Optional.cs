using System.Collections;

namespace Atelier.Framework.Requisitions.Requirement.Option
{
    public readonly struct Optional<T> : IEquatable<Optional<T>>
    {
        private readonly T? _value;
        private readonly bool _hasValue;

        private Optional(T value)
        {
            _value = value;
            _hasValue = true;
        }

        public static Optional<T> None => new();
        public static Optional<T> Some(T value) => new(value);

        public bool HasValue => _hasValue;
        public T Value => _hasValue ? _value! : throw new InvalidOperationException("Optional has no value");

        public T GetValueOrDefault(T defaultValue = default!) => _hasValue ? _value! : defaultValue;

        public Optional<TResult> Map<TResult>(Func<T, TResult> mapper)
        {
            return _hasValue ? Optional<TResult>.Some(mapper(_value!)) : Optional<TResult>.None;
        }

        public Optional<TResult> Bind<TResult>(Func<T, Optional<TResult>> binder)
        {
            return _hasValue ? binder(_value!) : Optional<TResult>.None;
        }

        public TResult Match<TResult>(Func<T, TResult> some, Func<TResult> none)
        {
            return _hasValue ? some(_value!) : none();
        }

        public void Match(Action<T> some, Action none)
        {
            if (_hasValue)
            {
                some(_value!);
            }
            else
            {
                none();
            }
        }

        public Optional<T> Where(Func<T, bool> predicate)
        {
            return _hasValue && predicate(_value!) ? this : None;
        }

        public static implicit operator Optional<T>(T value) => Some(value);
        public static explicit operator T(Optional<T> optional) => optional.Value;

        public bool Equals(Optional<T> other)
        {
            if (!_hasValue && !other._hasValue)
            {
                return true;
            }

            if (_hasValue != other._hasValue)
            {
                return false;
            }

            return EqualityComparer<T>.Default.Equals(_value, other._value);
        }

        public override bool Equals(object? obj) => obj is Optional<T> other && Equals(other);

        public override int GetHashCode()
        {
            return _hasValue ? _value?.GetHashCode() ?? 0 : 0;
        }

        public override string ToString()
        {
            return _hasValue ? $"Some({_value})" : "None";
        }

        public static bool operator ==(Optional<T> left, Optional<T> right) => left.Equals(right);
        public static bool operator !=(Optional<T> left, Optional<T> right) => !left.Equals(right);
    }

    public static class OptionalExtensions
    {
        public static Optional<T> ToOptional<T>(this T? value) where T : class
        {
            return value != null ? Optional<T>.Some(value) : Optional<T>.None;
        }

        public static Optional<T> ToOptional<T>(this T? value) where T : struct
        {
            return value.HasValue ? Optional<T>.Some(value.Value) : Optional<T>.None;
        }

        public static Optional<string> ToOptional(this string? value)
        {
            return string.IsNullOrEmpty(value) ? Optional<string>.None : Optional<string>.Some(value);
        }

        public static Optional<T> FirstOrNone<T>(this IEnumerable<T> source)
        {
            foreach (var item in source)
            {
                return Optional<T>.Some(item);
            }

            return Optional<T>.None;
        }

        public static Optional<T> FirstOrNone<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            foreach (var item in source)
            {
                if (predicate(item))
                {
                    return Optional<T>.Some(item);
                }
            }

            return Optional<T>.None;
        }
    }
}
