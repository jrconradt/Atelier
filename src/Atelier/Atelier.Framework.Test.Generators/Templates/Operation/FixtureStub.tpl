        [GeneratedTest("Operation/No-Throw-On-Default-Input", "{{ target }}")]
        public static void Test_Op_NoThrow_{{ opName }}_NeedsFixture_{{ paramCount }}()
        {
            throw new NeedsFixtureException("Class has a user-declared constructor; provide a TestFixtures.Register fixture to exercise [Operation] methods.");
        }
