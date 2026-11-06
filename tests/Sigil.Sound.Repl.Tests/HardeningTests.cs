using HostApi;

namespace Sigil.Sound.Repl.Tests
{
    /// <summary>
    /// Regression tests for the holes found when the REPL sandbox was probed (a user-written #line, recursive lambdas,
    /// a loop budget that false-tripped on legitimate per-sample code) and for the host-supplied <see cref="SnippetProfile"/>.
    /// </summary>
    public class HardeningTests
    {
        // --- Preprocessor directives -------------------------------------------------------------------------------

        [Fact]
        public void Line_directive_cannot_hide_a_blocked_api_from_the_validator()
        {
            // Before the fix this compiled and ran: the "other" file name made the validator skip every symbol after it.
            var result = SnippetCompiler.Compile("""
                #line 1 "other"
                var hit = System.IO.File.Exists("C:/Windows/win.ini");
                #line 3 "snippet"
                Sine(hit ? 440 : 220)
                """);

            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, d => d.IsError && d.Id == "SIGIL_SEC12");
        }

        [Theory]
        [InlineData("#pragma warning disable CS0168\nSine(440)")]
        [InlineData("#if true\nvar a = 1;\n#endif\nSine(440)")]
        [InlineData("#region x\n#endregion\nSine(440)")]
        [InlineData("#nullable enable\nSine(440)")]
        [InlineData("#line hidden\nSine(440)")]
        // Looks exactly like the wrapper's own directive, so only its position tells it apart.
        [InlineData("#line 1 \"snippet\"\nSine(440)")]
        [InlineData("var a = 1;\n#line default\nSine(440)")]
        public void Every_user_directive_is_rejected(string code)
        {
            var result = SnippetCompiler.Compile(code);

            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, d => d.IsError && d.Id == "SIGIL_SEC12");
        }

        [Fact]
        public void Directive_diagnostic_points_at_the_directive_in_the_users_text()
        {
            var diagnostics = SnippetCompiler.Check("var a = 1;\nvar b = 2;\n  #line 7 \"x\"\nSine(440)");

            var error = Assert.Single(diagnostics, d => d.Id == "SIGIL_SEC12");
            Assert.Equal(3, error.StartLine);
            Assert.Equal(3, error.StartColumn);
        }

        [Fact]
        public void Directive_text_inside_a_string_literal_is_just_text()
        {
            var result = SnippetCompiler.Compile("var note = \"#line 1 \\\"x\\\"\";\nSine(note.Length)");

            Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        }

        [Fact]
        public void Check_reports_directives_while_typing()
        {
            Assert.Contains(SnippetCompiler.Check("#line 1 \"other\"\nSine(440)"), d => d.IsError && d.Id == "SIGIL_SEC12");
        }

        // --- Recursion through lambdas -------------------------------------------------------------------------------

        // Each of these overflowed the stack and ended the process before lambdas were guarded.
        [Theory]
        [InlineData("Func<int, int> f = null;\nf = x => f(x + 1) + 1;\nSine(f(0))")]
        [InlineData("Func<int, int> f = null;\nf = (int x) => f(x + 1) + 1;\nSine(f(0))")]
        [InlineData("Func<int, int> f = null;\nf = delegate (int x) { return f(x + 1); };\nSine(f(0))")]
        [InlineData("Action a = null;\na = () => a();\na();\nSine(440)")]
        [InlineData("Func<int, int> f = null;\nf = x => { return f(x + 1); };\nSine(f(0))")]
        public void Recursive_lambdas_trip_the_depth_guard_instead_of_overflowing_the_stack(string code)
        {
            var result = SnippetCompiler.Compile(code);

            Assert.True(result.Success, string.Join("\n", result.Diagnostics));
            var ex = Assert.Throws<ExecutionLimitException>(() => result.Run());
            Assert.Contains("Recursion depth limit exceeded", ex.Message);
        }

        [Fact]
        public void Mutually_recursive_lambdas_trip_the_depth_guard()
        {
            var result = SnippetCompiler.Compile("""
                Func<int, int> a = null;
                Func<int, int> b = null;
                a = x => b(x + 1);
                b = x => a(x + 1);
                Sine(a(0))
                """);

            Assert.True(result.Success, string.Join("\n", result.Diagnostics));
            Assert.Throws<ExecutionLimitException>(() => result.Run());
        }

        [Fact]
        public void Recursion_inside_a_per_sample_function_trips_the_guard_while_rendering()
        {
            var result = SnippetCompiler.Compile("Func<double, double> f = null;\nf = t => f(t) + 1;\nFn(t => f(t))");

            Assert.True(result.Success, string.Join("\n", result.Diagnostics));
            var sound = result.Run();
            Assert.Throws<ExecutionLimitException>(() => sound.Render(0.01));
        }

        [Fact]
        public void The_depth_counter_comes_back_down_so_legitimate_nesting_keeps_working()
        {
            // 100 levels deep, called many times: only a counter that leaks on exit would trip the 256-deep limit.
            var result = SnippetCompiler.Compile("""
                Func<int, int> down = null;
                down = n => n == 0 ? 0 : 1 + down(n - 1);
                var total = 0;
                for (var i = 0; i < 50; i++) total += down(100);
                Sine(total)
                """);

            Assert.True(result.Success, string.Join("\n", result.Diagnostics));
            Assert.Equal("Sine(5000)", result.Run().ToString());
        }

        [Fact]
        public void Guarded_lambdas_keep_their_meaning_for_values_and_for_void_delegates()
        {
            var result = SnippetCompiler.Compile("""
                var seen = new List<int>();
                Array.ForEach(new[] { 1, 2, 3 }, x => seen.Add(x));
                var doubled = seen.Select(x => x * 2).Sum();
                Sine(doubled)
                """);

            Assert.True(result.Success, string.Join("\n", result.Diagnostics));
            Assert.Equal("Sine(12)", result.Run().ToString());
        }

        [Fact]
        public void An_exception_thrown_inside_a_lambda_still_releases_the_depth_counter()
        {
            var result = SnippetCompiler.Compile("""
                Func<int, int> boom = x => throw new InvalidOperationException("no");
                var caught = 0;
                for (var i = 0; i < 400; i++)
                {
                    try { boom(i); } catch (InvalidOperationException) { caught++; }
                }
                Sine(caught)
                """);

            Assert.True(result.Success, string.Join("\n", result.Diagnostics));
            Assert.Equal("Sine(400)", result.Run().ToString());
        }

        [Fact]
        public void Expression_bodied_functions_that_throw_are_guarded_without_breaking()
        {
            var result = SnippetCompiler.Compile("""
                int Boom(int x) => throw new InvalidOperationException("no");
                void Quiet(int x) => Console_free(x);
                void Console_free(int x) { }
                var caught = 0;
                try { Quiet(1); Boom(1); } catch (InvalidOperationException) { caught++; }
                Sine(caught)
                """);

            Assert.True(result.Success, string.Join("\n", result.Diagnostics));
            Assert.Equal("Sine(1)", result.Run().ToString());
        }

        // --- Loop budget ---------------------------------------------------------------------------------------------

        private const string Additive128 =
            "Fn(t => { float s = 0; for (int k = 1; k <= 128; k++) s += (float)Sin(2 * PI * 110 * k * t) / k; return s * 0.1f; })";

        private static void RenderFrames(Sound sound, int frames) => RenderPeak(sound, frames);

        private static float RenderPeak(Sound sound, int frames)
        {
            var op = sound.Compile(RenderContext.Default);
            var block = new StereoFrame[RenderContext.DefaultBlockSize];
            float peak = 0;
            for (int done = 0; done < frames; done += block.Length)
            {
                op.RenderBlock(block);
                foreach (var frame in block)
                    peak = Math.Max(peak, Math.Abs(frame.Left));
            }
            return peak;
        }

        [Fact]
        public void A_128_partial_additive_voice_fits_the_budget_for_the_frames_being_rendered()
        {
            var sound = SnippetCompiler.Compile(Additive128).Run();

            // Before the budget grew with frames, this threw after 1,536 frames: 128 steps a frame against 200,000 in total.
            ExecutionGuard.Reset(frames: 2400);
            float peak = RenderPeak(sound, 2400);

            Assert.True(peak > 0.01f, "the voice rendered silence instead of running to the end");
        }

        [Fact]
        public void The_same_voice_does_not_fit_when_the_host_announces_no_frames()
        {
            var sound = SnippetCompiler.Compile(Additive128).Run();

            ExecutionGuard.Reset();
            Assert.Throws<ExecutionLimitException>(() => RenderFrames(sound, 2400));
        }

        [Fact]
        public void An_endless_loop_still_stops_when_the_budget_covers_a_block()
        {
            var sound = SnippetCompiler.Compile("Sine(440).Through(input => Operator.From(block => { while (true) { } }))").Run();

            ExecutionGuard.Reset(frames: 2400);
            Assert.Throws<ExecutionLimitException>(() => RenderFrames(sound, 256));
        }

        // --- Diagnostics keep pointing at the user's lines -----------------------------------------------------------

        [Theory]
        [InlineData("for (int i = 0; i < 3; i++) { }\nvar x = missing;\nSine(440)", 2)]
        [InlineData("while (false) { }\ndo { } while (false);\nvar x = missing;\nSine(440)", 3)]
        [InlineData("Func<int, int> f = x => x + 1;\nvar y = missing;\nSine(f(1))", 2)]
        [InlineData("Func<int, int> f = x => { return x + 1; };\nvar y = missing;\nSine(f(1))", 2)]
        public void Compile_errors_after_guarded_constructs_report_the_right_line(string code, int expectedLine)
        {
            var result = SnippetCompiler.Compile(code);

            Assert.False(result.Success);
            var error = Assert.Single(result.Diagnostics, d => d.IsError && d.Id == "CS0103");
            Assert.Equal(expectedLine, error.StartLine);
        }

        // --- Host profiles -------------------------------------------------------------------------------------------

        private static SnippetProfile HostProfile(bool allowNamespace = true) => new(
            extraUsings: ["using static HostApi.Host;"],
            extraAssemblies: [typeof(Host).Assembly],
            extraAllowedNamespaces: allowNamespace ? ["HostApi"] : []);

        [Fact]
        public void A_host_profile_adds_its_own_api_to_the_snippet()
        {
            var result = SnippetCompiler.Compile("Sine(Cutoff)", HostProfile());

            Assert.True(result.Success, string.Join("\n", result.Diagnostics));
            Assert.Equal("Sine(1200)", result.Run().ToString());
        }

        [Fact]
        public void A_host_api_is_unreachable_unless_the_profile_also_allows_its_namespace()
        {
            var result = SnippetCompiler.Compile("Sine(Cutoff)", HostProfile(allowNamespace: false));

            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, d => d.IsError && d.Id == "SIGIL_SEC08");
        }

        [Fact]
        public void The_default_profile_does_not_know_the_host_api()
        {
            Assert.False(SnippetCompiler.Compile("Sine(Cutoff)").Success);
        }

        [Theory]
        [InlineData("System.IO")]
        [InlineData("System.Reflection")]
        [InlineData("System.Diagnostics")]
        [InlineData("Sigil.Sound.Repl")]
        public void A_profile_cannot_unblock_what_the_base_policy_blocks(string ns)
        {
            var profile = new SnippetProfile(extraAllowedNamespaces: [ns]);

            Assert.True(profile.IsBlockedNamespace(ns));
            Assert.False(profile.IsAllowedNamespace(ns));
            Assert.False(SnippetCompiler.Compile("System.IO.File.Exists(\"x\");\nSine(440)", profile).Success);
        }

        [Fact]
        public void A_profile_can_block_more_than_the_base_policy()
        {
            var profile = new SnippetProfile(extraBlockedNamespaces: ["System.Collections.Generic"], extraForbiddenSystemTypes: ["Random"]);

            Assert.True(SnippetCompiler.Compile("var l = new List<int>();\nSine(440)").Success);
            Assert.Contains(SnippetCompiler.Compile("var l = new List<int>();\nSine(440)", profile).Diagnostics, d => d.Id == "SIGIL_SEC08");
            Assert.True(SnippetCompiler.Compile("Sine(new Random(1).Next(100, 200))").Success);
            Assert.Contains(SnippetCompiler.Compile("Sine(new Random(1).Next(100, 200))", profile).Diagnostics, d => d.Id == "SIGIL_SEC08");
        }

        [Fact]
        public void Editor_completion_follows_the_profile_instead_of_a_second_list()
        {
            var profile = new SnippetProfile(extraBlockedNamespaces: ["System.Collections.Generic"]);
            const string code = "System.Collections.";

            Assert.Contains(EditorServices.Complete(code, code.Length), c => c.Label == "Generic");
            Assert.DoesNotContain(EditorServices.Complete(code, code.Length, profile), c => c.Label == "Generic");
        }

        [Fact]
        public void Directives_are_rejected_under_a_host_profile_too()
        {
            var result = SnippetCompiler.Compile("#line 1 \"other\"\nSine(Cutoff)", HostProfile());

            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, d => d.Id == "SIGIL_SEC12");
        }
    }
}

namespace HostApi
{
    /// <summary>Stands in for a host's own snippet API, in a namespace the base policy knows nothing about.</summary>
    public static class Host
    {
        public static double Cutoff => 1200;
    }
}
