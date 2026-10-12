using SchoolKiosk;

var checks = 0;
void Check(bool value, string name) { checks++; if (!value) throw new Exception(name); }
var monday = new DateTime(2026, 10, 12);
for (var day = 0; day < 14; day++)
for (var minute = 0; minute < 1440; minute++)
{
    var now = monday.AddDays(day).AddMinutes(minute);
    var expected = day % 7 < 5 && minute >= 510 && minute < 930;
    Check(SchoolSchedule.IsActive(now) == expected, $"active {now}");
    var next = SchoolSchedule.NextChange(now);
    Check(next > now, $"future {now}");
    Check(SchoolSchedule.IsActive(next.AddTicks(-1)) != SchoolSchedule.IsActive(next), $"boundary {next}");
}
Check(SchoolSchedule.NextChange(monday.AddDays(4).AddHours(16)) == monday.AddDays(7).AddHours(8).AddMinutes(30), "Friday skips weekend");
Check(!SchoolSchedule.IsActive(monday.AddHours(8).AddMinutes(30).AddTicks(-1)), "before start");
Check(!SchoolSchedule.IsActive(monday.AddHours(15).AddMinutes(30)), "end exclusive");
Console.WriteLine($"PASS: {checks} schedule assertions (14 complete days, boundaries and weekends).");
var own = "com.ahura.schoolkiosk";
var keyboard = "example.keyboard";
Check(!WifiReturnPolicy.ShouldReturn(null, own, keyboard), "ignore missing event package");
Check(!WifiReturnPolicy.ShouldReturn(own, own, keyboard), "allow kiosk");
Check(!WifiReturnPolicy.ShouldReturn("com.android.settings", own, keyboard), "allow Wi-Fi settings");
Check(!WifiReturnPolicy.ShouldReturn(keyboard, own, keyboard), "allow password keyboard");
Check(WifiReturnPolicy.ShouldReturn("com.hihonor.android.launcher", own, keyboard), "return from Home");
Check(!WifiReturnPolicy.ShouldReturn("com.android.systemui", own, keyboard), "allow Android pin/unpin transition windows");
Check(WifiReturnPolicy.ShouldReturn("example.otherapp", own, keyboard), "return from other apps");
Console.WriteLine("PASS: 7 Wi-Fi return policy assertions.");
