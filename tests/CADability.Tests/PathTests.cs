using CADability.GeoObject;

namespace CADability.Tests
{
	[TestClass]
	public class PathTests
    {
		[TestMethod]
		public void IsInPlane_Works_WhenLinear()
		{
			var path = GeoObject.Path.Construct();
			path.Set([
                Line.MakeLine(new(0, 0, 0), new(10, 0, 0)),
                Line.MakeLine(new(10, 0, 0), new(20, 0, 0))
			]);

			Assert.IsTrue(path.IsInPlane(Plane.XYPlane));
			Assert.IsTrue(path.IsInPlane(Plane.XZPlane));
			Assert.IsFalse(path.IsInPlane(Plane.YZPlane));
		}
	}
}
