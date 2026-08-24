using Exb.Core.Forms;
using Xunit;

namespace Exb.Tests;

public class FormTests
{
    private static Dictionary<string, string[]> Post(params (string Key, string Value)[] values)
        => values.GroupBy(v => v.Key)
                 .ToDictionary(g => g.Key, g => g.Select(v => v.Value).ToArray(), StringComparer.OrdinalIgnoreCase);

    // --- validating what a visitor submitted ---------------------------------

    [Fact]
    public void SplitsAnswersBetweenRealColumnsAndTheJsonProfile()
    {
        var form = FormDefaults.Visitor();

        var result = FormValidator.Validate(form, Post(
            ("fullName", "Sara Khan"),
            ("email", "Sara.Khan@Example.COM"),
            ("badgeEpc", "3034257BF4A1B2C3D4E5F607"),
            ("company", "Meridian Systems"),
            ("consentTracking", "true"),
            ("consentEmail", "true"),
            ("visitorType", "buyer"),
            ("budgetWindow", "6m")), adminContext: true);

        Assert.True(result.IsValid);

        // The system acts on these, so they are real columns.
        Assert.Equal("Sara Khan", result.Core<string>("FullName"));
        Assert.Equal("sara.khan@example.com", result.Core<string>("Email"));   // normalised
        Assert.True(result.Core<bool>("ConsentTracking"));

        // These are this organiser's own questions, so they go to the profile.
        Assert.Equal("buyer", result.ProfileValues["visitorType"]);
        Assert.Equal("6m", result.ProfileValues["budgetWindow"]);
        Assert.DoesNotContain("visitorType", result.CoreValues.Keys);
    }

    [Fact]
    public void RequiredAnswersAreEnforcedWithTheAdminsOwnLabel()
    {
        var form = FormDefaults.Visitor();
        var result = FormValidator.Validate(form, Post(("email", "someone@example.com")), adminContext: true);

        Assert.False(result.IsValid);
        Assert.Contains("fullName", result.Errors.Keys);
        Assert.Contains("Full name", result.Errors["fullName"]);
    }

    [Fact]
    public void AnUncheckedConsentBoxIsADefiniteNoRatherThanAMissingAnswer()
    {
        var form = FormDefaults.Visitor();

        var result = FormValidator.Validate(form, Post(
            ("fullName", "Test Visitor"),
            ("email", "t@example.com"),
            ("badgeEpc", "ABC123"),
            ("visitorType", "buyer"),
            ("consentTracking", ""),
            ("consentEmail", "true")), adminContext: true);

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => $"{e.Key}: {e.Value}")));
        Assert.False(result.Core<bool>("ConsentTracking"));
        Assert.True(result.Core<bool>("ConsentEmail"));
    }

    [Fact]
    public void MalformedEmailAndOffMenuChoicesAreRejected()
    {
        var form = FormDefaults.Visitor();

        var result = FormValidator.Validate(form, Post(
            ("fullName", "Test"),
            ("email", "not-an-address"),
            ("badgeEpc", "ABC"),
            ("visitorType", "smuggled-value")), adminContext: true);

        Assert.False(result.IsValid);
        Assert.Contains("email", result.Errors.Keys);
        Assert.Contains("visitorType", result.Errors.Keys);
    }

    [Fact]
    public void AnAdminAddedPatternIsEnforcedWithoutADeployment()
    {
        var form = FormDefaults.Exhibitor();
        form.Sections[0].Fields.Add(new FormField
        {
            Key = "vatNumber",
            Label = "VAT number",
            Type = FormFieldType.Text,
            Pattern = "^[0-9]{15}$",
            PatternMessage = "A VAT number is exactly 15 digits.",
        });

        var bad = FormValidator.Validate(form, Post(
            ("companyName", "Acme"), ("categoryId", "1"), ("vatNumber", "12345")));
        Assert.Contains("A VAT number is exactly 15 digits.", bad.Errors["vatNumber"]);

        var good = FormValidator.Validate(form, Post(
            ("companyName", "Acme"), ("categoryId", "1"), ("vatNumber", "123456789012345")));
        Assert.True(good.IsValid);
    }

    [Fact]
    public void APathologicalPatternCannotHangTheRegistrationDesk()
    {
        var form = FormDefaults.Exhibitor();
        form.Sections[0].Fields.Add(new FormField
        {
            Key = "trouble",
            Label = "Trouble",
            Type = FormFieldType.Text,
            Pattern = "^(a+)+$",   // catastrophic backtracking
        });

        var result = FormValidator.Validate(form, Post(
            ("companyName", "Acme"), ("categoryId", "1"),
            ("trouble", new string('a', 40) + "!")));

        // Either it matched quickly or it timed out; what matters is that it returned.
        Assert.False(result.IsValid);
        Assert.Contains("trouble", result.Errors.Keys);
    }

    [Fact]
    public void StaffOnlyFieldsAreHiddenFromVisitorFacingPages()
    {
        var form = FormDefaults.Visitor();

        var publicSubmission = FormValidator.Validate(form, Post(
            ("fullName", "Public User"),
            ("email", "p@example.com"),
            ("visitorType", "buyer"),
            ("badgeEpc", "SNEAKY")), adminContext: false);

        // The badge field is staff-only, so a self-service page cannot set it.
        Assert.DoesNotContain("BadgeEpc", publicSubmission.CoreValues.Keys);
        Assert.True(publicSubmission.IsValid, string.Join("; ", publicSubmission.Errors.Select(e => $"{e.Key}: {e.Value}")));
    }

    // --- validating a rearranged layout --------------------------------------

    [Fact]
    public void TheBuiltInLayoutsAreValid()
    {
        Assert.Empty(FormValidator.ValidateLayout(FormDefaults.Visitor()));
        Assert.Empty(FormValidator.ValidateLayout(FormDefaults.Exhibitor()));
    }

    [Fact]
    public void DroppingTheEmailFieldIsRefusedRatherThanFailingThatEvening()
    {
        var form = FormDefaults.Visitor();
        Assert.True(form.RemoveField("email"));

        var problems = FormValidator.ValidateLayout(form);

        Assert.NotEmpty(problems);
        Assert.Contains(problems, p => p.Contains("Email address"));
        Assert.Contains(problems, p => p.Contains("e-catalogue pack"));
    }

    [Fact]
    public void TurningOffARequiredCoreFieldCountsAsDroppingIt()
    {
        var form = FormDefaults.Exhibitor();
        form.Field("categoryId")!.Enabled = false;

        var problems = FormValidator.ValidateLayout(form);
        Assert.Contains(problems, p => p.Contains("Category"));
    }

    [Fact]
    public void DuplicateKeysAndDuplicateBindingsAreCaught()
    {
        var form = FormDefaults.Visitor();
        form.Sections[0].Fields.Add(new FormField { Key = "email", Label = "Second email", Type = FormFieldType.Email });
        form.Sections[0].Fields.Add(new FormField
        {
            Key = "otherEmail", Label = "Another", Type = FormFieldType.Email, CoreProperty = "Email",
        });

        var problems = FormValidator.ValidateLayout(form);

        Assert.Contains(problems, p => p.Contains("used more than once"));
        Assert.Contains(problems, p => p.Contains("bound to 'Email'"));
    }

    [Fact]
    public void ASelectWithNoOptionsIsCaught()
    {
        var form = FormDefaults.Visitor();
        form.Sections[1].Fields.Add(new FormField { Key = "empty", Label = "Empty choice", Type = FormFieldType.Select });

        Assert.Contains(FormValidator.ValidateLayout(form), p => p.Contains("no options"));
    }

    // --- rearranging ---------------------------------------------------------

    [Fact]
    public void FieldsMoveWithinAndBetweenSections()
    {
        var form = FormDefaults.Visitor();
        var section = form.Sections[0];
        string first = section.Fields[0].Key;
        string second = section.Fields[1].Key;

        Assert.True(form.MoveField(second, -1));
        Assert.Equal(second, section.Fields[0].Key);
        Assert.Equal(first, section.Fields[1].Key);

        string target = form.Sections[1].Id;
        Assert.True(form.MoveFieldToSection(second, target));
        Assert.DoesNotContain(section.Fields, f => f.Key == second);
        Assert.Contains(form.Sections[1].Fields, f => f.Key == second);

        // Still a valid layout: moving a core field is allowed, losing it is not.
        Assert.Empty(FormValidator.ValidateLayout(form));
    }

    [Fact]
    public void MovingBeyondTheEndsIsRefusedRatherThanWrappingAround()
    {
        var form = FormDefaults.Visitor();
        string firstKey = form.Sections[0].Fields[0].Key;

        Assert.False(form.MoveField(firstKey, -1));
        Assert.Equal(firstKey, form.Sections[0].Fields[0].Key);
    }

    [Fact]
    public void ADragReorderThatOmitsFieldsKeepsThemRatherThanDroppingThem()
    {
        var form = FormDefaults.Visitor();
        var section = form.Sections[0];
        int before = section.Fields.Count;

        // A stale browser tab sends only the two keys it knew about.
        Assert.True(form.ReorderSection(section.Id, [section.Fields[2].Key, section.Fields[0].Key]));

        Assert.Equal(before, section.Fields.Count);
        Assert.Empty(FormValidator.ValidateLayout(form));
    }

    [Fact]
    public void SectionsReorderAndTheLayoutSurvivesAJsonRoundTrip()
    {
        var form = FormDefaults.Exhibitor();
        string second = form.Sections[1].Id;

        Assert.True(form.MoveSection(second, -1));
        Assert.Equal(second, form.Sections[0].Id);

        var restored = FormDefinition.FromJson(form.ToJson());

        Assert.Equal(form.Sections.Count, restored.Sections.Count);
        Assert.Equal(second, restored.Sections[0].Id);
        Assert.Equal(form.AllFields.Count(), restored.AllFields.Count());
        Assert.Empty(FormValidator.ValidateLayout(restored));
    }
}
