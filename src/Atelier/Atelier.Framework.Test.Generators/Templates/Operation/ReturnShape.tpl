        [GeneratedTest("Operation/Returns-Outcome-Shape", "{{ target }}")]
        public static void Test_Op_ReturnShape_{{ suffix }}()
        {
            throw new InvalidOperationException("Method {{ opName }} returns '{{ returnType }}' — not Outcome-shaped (Outcome | Outcome<T> | Task<Outcome[<T>]> | ValueTask<Outcome[<T>]>)");
        }
