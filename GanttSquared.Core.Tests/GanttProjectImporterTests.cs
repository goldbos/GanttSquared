using System.Xml.Linq;
using GanttSquared.Core.Model;
using GanttSquared.Core.Persistence;

namespace GanttSquared.Core.Tests;

public class GanttProjectImporterTests
{
    // Trimmed from GanttProject's own HouseBuildingSample.gan: a parent task with two nested
    // children (one a milestone), an FS dependency between them, a resource, and an allocation.
    private const string SampleXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <project name="House Building" company="" webLink="" view-date="2024-05-19" version="3.3.3309" locale="en">
            <description/>
            <calendars>
                <day-types>
                    <default-week id="1" name="default" sun="1" mon="0" tue="0" wed="0" thu="0" fri="0" sat="1"/>
                </day-types>
            </calendars>
            <tasks empty-milestones="true">
                <task id="0" name="Architectural design" meeting="false" start="2024-05-27" duration="25" complete="85">
                    <task id="9" name="Create draft of architecture" meeting="false" start="2024-05-27" duration="10" complete="100" priority="2">
                        <depend id="17" type="2" difference="0" hardness="Strong"/>
                    </task>
                    <task id="17" name="Agreement on architectural plan" meeting="true" start="2024-06-10" duration="0" complete="0"/>
                </task>
                <task id="49" name="Low priority follow-up" meeting="false" start="2024-05-27" duration="5" complete="0" priority="3"/>
            </tasks>
            <resources>
                <resource id="1" name="Jack House" function="Default:1" contacts="jack@example.com" phone="0044 077345456"/>
            </resources>
            <allocations>
                <allocation task-id="9" resource-id="1" function="Default:1" responsible="false" load="50.0"/>
            </allocations>
            <vacations/>
            <previous/>
            <roles roleset-name="Default"/>
            <roles/>
        </project>
        """;

    [Fact]
    public void Import_ParsesHierarchyDatesMilestonesAndDependencies()
    {
        var project = GanttProjectImporter.Import(XDocument.Parse(SampleXml));

        Assert.Equal("House Building", project.Name);
        Assert.Equal(4, project.Tasks.Count);

        var parent = project.Tasks.Single(t => t.Name == "Architectural design");
        var draft = project.Tasks.Single(t => t.Name == "Create draft of architecture");
        var agreement = project.Tasks.Single(t => t.Name == "Agreement on architectural plan");
        var followUp = project.Tasks.Single(t => t.Name == "Low priority follow-up");

        // Hierarchy: draft and agreement are children of parent.
        Assert.Equal(parent.Id, draft.ParentId);
        Assert.Equal(parent.Id, agreement.ParentId);
        Assert.Null(parent.ParentId);

        // duration="10" working days from Mon 2024-05-27 lands on Mon 2024-06-10 (skipping the
        // two intervening weekends) - the whole point of AddWorkingDays over naive calendar days.
        Assert.Equal(new DateOnly(2024, 5, 27), draft.StartDate);
        Assert.Equal(new DateOnly(2024, 6, 10), draft.EndDate);
        Assert.Equal(100, draft.ProgressPercent);
        Assert.Equal(PriorityLevel.High, draft.Priority); // persistent "2"

        // Milestone: duration forced to 0 regardless of the file's own duration attribute.
        Assert.True(agreement.IsMilestone);
        Assert.Equal(agreement.StartDate, agreement.EndDate);

        Assert.Equal(PriorityLevel.Low, followUp.Priority); // persistent "3" (Lowest) maps down to Low

        // Dependency: draft (predecessor) -> agreement (successor), FS, no lag.
        var dependency = Assert.Single(project.Dependencies);
        Assert.Equal(draft.Id, dependency.PredecessorTaskId);
        Assert.Equal(agreement.Id, dependency.SuccessorTaskId);
        Assert.Equal(DependencyType.FinishToStart, dependency.Type);
        Assert.Equal(0, dependency.LagDays);
    }

    [Fact]
    public void Import_ParsesResourcesAndAllocations()
    {
        var project = GanttProjectImporter.Import(XDocument.Parse(SampleXml));

        var resource = Assert.Single(project.Resources);
        Assert.Equal("Jack House", resource.Name);
        Assert.Equal("jack@example.com", resource.Email);

        var draft = project.Tasks.Single(t => t.Name == "Create draft of architecture");
        Assert.Contains(resource.Id, draft.AssignedResourceIds);
    }

    [Fact]
    public void Import_MissingRootElement_Throws()
    {
        var doc = XDocument.Parse("<not-a-project/>");
        Assert.Throws<InvalidDataException>(() => GanttProjectImporter.Import(doc));
    }
}
