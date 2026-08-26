using CADability.GeoObject;

namespace CADability.Tests
{
	[TestClass]
	public class CurvesTests
	{
		[TestMethod]
		public void GetCommonPlane_ShouldFail_WhenCurvesAreColinear()
		{
			ICurve[] curves =
			[
				Line.MakeLine(new GeoPoint(0, 0, 0), new GeoPoint(10, 10, 0)),
				Line.MakeLine(new GeoPoint(10, 10, 0), new GeoPoint(20, 20, 0))
			];

			Assert.IsFalse(Curves.GetCommonPlane(curves, out _));
			Assert.IsFalse(Curves.GetCommonPlane(curves[0], curves[1], out _));
		}

		[TestMethod]
		public void GetCommonPlane_ShouldFindPlane_WhenLinesSpanIt()
		{
			ICurve[] curves =
			[
				Line.MakeLine(new GeoPoint(0, 0, 5), new GeoPoint(10, 0, 5)),
				Line.MakeLine(new GeoPoint(10, 0, 5), new GeoPoint(10, 10, 5))
			];

			Assert.IsTrue(Curves.GetCommonPlane(curves, out Plane pln));
			Assert.IsTrue(Precision.SameDirection(pln.Normal, GeoVector.ZAxis, false));
			Assert.IsTrue(curves[0].IsInPlane(pln) && curves[1].IsInPlane(pln));
		}

		[TestMethod]
		public void GetCommonPlane_ShouldFail_WhenLinesAreSkew()
		{
			ICurve[] curves =
			[
				Line.MakeLine(new GeoPoint(0, 0, 0), new GeoPoint(10, 0, 0)),
				Line.MakeLine(new GeoPoint(0, 5, 5), new GeoPoint(0, 5, 15))
			];

			Assert.IsFalse(Curves.GetCommonPlane(curves, out _));
		}
	}
}
