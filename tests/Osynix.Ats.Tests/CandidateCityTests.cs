using Osynix.Ats.Domain;
using Xunit;
namespace Osynix.Ats.Tests;
public class CandidateCityTests {
 [Theory]
 [InlineData("House 12, Street 3, Gulberg, Lahore, Punjab, Pakistan","Lahore")]
 [InlineData("DHA Phase 6, Karachi","Karachi")]
 [InlineData("Bahria Town, Rawalpindi","Rawalpindi")]
 [InlineData("Apartment 4, Abu Dhabi, UAE","Abu Dhabi")]
 [InlineData("New Delhi, India","New Delhi")]
 [InlineData("Gulberg"," ")]
 [InlineData("House 12, Street 5"," ")]
 [InlineData("Lahore / Karachi"," ")]
 public void LegacyAddressesYieldOnlyAnUnambiguousCity(string value,string expected)=>Assert.Equal(expected.Trim(),CandidateCity.FromLegacy(value));
 [Theory][InlineData("Lahore","Lahore")][InlineData("Cape Town","Cape Town")][InlineData("Reykjavik","Reykjavik")][InlineData("  San   Jose  ","San Jose")][InlineData("Sector G-11"," ")][InlineData("Model Town"," ")][InlineData("House 19, Unknown Area"," ")]
 public void ExplicitCityInputDoesNotRetainAddressDetails(string input,string expected)=>Assert.Equal(expected.Trim(),CandidateCity.Explicit(input));
 [Fact]public void ExistingCandidateAddressIsNotRenderedAsTheCity(){var candidate=new Candidate{Location="House 12, Street 3, Gulberg, Lahore, Pakistan"};Assert.Equal("Lahore",CandidateCity.Display(candidate));candidate.City="Reykjavik";Assert.Equal("Reykjavik",CandidateCity.Display(candidate));Assert.Contains("House 12",candidate.Location);}
}
