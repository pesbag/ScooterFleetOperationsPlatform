exec(open('output/project-book/author.py',encoding='utf-8-sig').read().split("add('ספר פרויקט")[0])
sections=json.loads((OUT/'sections.json').read_text(encoding='utf-8'))
add('44. מילון הישויות התפעוליות','''FleetUser: Id מזהה מקומי; Name ו־Email פרטי זיהוי; PasswordHash תוצר גיבוב; Role תפקיד; Enabled הרשאת פעילות; SessionVersion גרסת ההתחברות; CreatedAt זמן יצירת החשבון.
FleetAlert: StationId התחנה הרלוונטית; Severity חומרה; Title ו־Description הסבר; Status מצב; CreatedAt, ResolvedAt ו־ResolvedBy תיעוד מחזור החיים. StationName הוא שדה NotMapped להעשרת תצוגה ואינו עמודה חדשה.
Recommendation: מזהי מקור ויעד, Quantity להעברה, Priority, Reason, Status, זמני יצירה ועדכון ומבצע העדכון. SourceStationName ו־DestinationStationName מיועדים לתצוגה ומסומנים NotMapped. כמות ההמלצה אינה כמות שהמערכת מעבירה פיזית.
RiderFavorite: זוג UserId ו־StationId. RiderIssue: מזהה דיווח, משתמש, תחנה, תיאור, מצב ותיעוד סגירה. אין בפרויקט ניהול מלאי של אופניים בודדים לפי מספר שלדה; יחידת המידע העיקרית היא התחנה.
TaskMeasurement: קשר להמלצה, זמני התחלה וסיום, כמות ביעד לפני ואחרי וזמן תצפית. ForecastSample: תחנה, זמן הנפקה, זמן יעד, כמות חזויה, כמות בפועל וזמן מדידה. שדות nullable מסמנים תצפית שטרם התקבלה, לא תוצאה של אפס.''', ['StationApi/Models/Operations.cs'],'public class TaskMeasurement',24)
add('45. חוזי DTO ומיפוי שמות','''DTO הוא אובייקט העברת נתונים, אך בפרויקט חלק ממחלקות המטא־דאטה משמשות גם ישויות EF. StationInformationDto כולל Station_id, Name, Lat, Lon ו־Capacity. VehicleTypesDto כולל מזהה סוג, גורם צורה וסוג הנעה. אותם רעיונות מופיעים בשירותים שונים כדי לשמר את חוזי התקשורת והאחסון הקיימים.
StationStatusDto בקליטה ובמעבד משמר כמויות, דגלים וזמנים nullable. קיים מיפוי ל־num_docks_available למרות שם פנימי היסטורי Num_docs_available. הסריאליזציה והפענוח נבדקים יחד כדי שלא תאבד משמעות null. שינוי כתיב פנימי ללא מיפוי מתאים עלול לשבור הודעות שכבר קיימות ב־Kafka או מסמכי MongoDB.
StationInput ו־StationAnalysis הם records של שכבת הניתוח, לא אותה ישות EF. Reading הוא זוג זמן וכמות; MoveSuggestion הוא תוצר חישובי לפני שמירה כהמלצה תפעולית. ההבחנה מאפשרת למנוע הניתוח לעבוד בלי DbContext.
AllDataDto ו־GetFiletrDto משרתים את הנתיב הוותיק. RegisterRequest, LoginRequest, UserRequest, StatusRequest ו־RiderIssueRequest מגדירים קלט לפעולות. UserView מגדיר פלט בטוח לחשבון. אין למחוק טיפוס רק בגלל שמו: צריך לבדוק את הצרכנים ואת חוזה JSON שלו.''', ['BikeFleetOperationPlatform/Dtos/StationStatusDto.cs'],None,24)
# File-level inventory, with method names taken from source rather than invented.
descriptions={
'Program.cs':'נקודת כניסה, רישום תלויות ואתחול תהליך; מוסברת בפרקי האתחול של השירות המתאים.',
'Operations.cs':'ישויות מסד התפעול, שדות מצב ותיעוד; מילון השדות מפורט בפרק הישויות.',
'GbfsFlagConverter.cs':'ממיר true/false או 0/1 לחוזה דגלי GBFS; ערך לא תקין גורם לשגיאת פענוח.',
'FleetCacheContract.cs':'קבועי TTL ומפתחות גרסה משותפים למעבד ול־API.',
'FleetCacheState.cs':'נעילת מילוי מטמון מקומית ומזהה דור מקומי.',
'OperationsSchema.cs':'הוספת טבלאות תפעול חסרות ללא מחיקת נתונים קיימים.',
'ViewText.cs':'פונקציות תצוגה ל־JSON: טקסט, מספר, זמן, שם, איכות נתונים ומצב שירות.',
'DurationText.cs':'המרת דקות לשעות ושארית, וטיפול בערכים חסרים או לא תקינים.',
'FleetApiClient.cs':'שליחת HTTP פנימי, העברת cookies ו־CSRF והמרת שגיאות ל־ApiFailure.',
'IKafkaMessageHandler.cs':'חוזה Topic ו־HandleAsync שמאפשר לצרכן לבחור מטפל לפי סוג הודעה.',
'IStationRepository.cs':'חוזה הקריאה של API התחנות הוותיק, לרשימה מסוננת ולפרטי תחנה.',
'AuthController.cs':'כניסה, הרשמה, יציאה, זהות נוכחית ו־CSRF; הרשמה קובעת RIDER בשרת.',
'UsersController.cs':'רשימה, יצירה, עריכה והשבתת משתמשים; הגנות על מנהל פעיל אחרון ועל sessions.',
'WorkspaceController.cs':'פעולות Views לתפעול, טפסים, הודעות והפניות לאחר כתיבה.',
'RiderController.cs':'פעולות Views לחיפוש, מועדפים ודיווחים של רוכב.',
'RiderApiController.cs':'API אישי לרוכב וגם Controller לפתרון דיווחים על ידי צוות.',
'FleetController.cs':'קריאות תחנות, היסטוריה, סטטוס וסיכום רשת עם העשרת מצב תפעולי.',
'OperationsController.cs':'קריאה ושינוי מצב של התראות והמלצות עם שמירה ופסילת מטמון.',
'MetricsController.cs':'endpoint מוגן שמחזיר Summary של מדדי התועלת.',
'StationController.cs':'שימור API קריאה ותיק באמצעות Repository וללא שינוי חוזה לקוחות.',
'StationRepository.cs':'שאילתות MongoDB, צירוף פרטי MySQL ומיפוי DTO לנתיב הוותיק.',
'SmartFleetEngine.cs':'ניתוח טהור של איכות נתונים, סיכון, מגמה והמלצות העברה.',
'FleetDataService.cs':'איחוד נתונים, מטמון רשימה ותחנה בודדת, מיפוי BSON והעשרת שמות.',
'StationStateService.cs':'חיבור בין סיכון מקור, תקלות פתוחות ומצב תפעולי.',
'OperationsService.cs':'מחזור יצירת התראות, ביטול המלצות לא אפשריות, המלצות חדשות וניתוח ברקע.',
'MetricsService.cs':'זמן ריק, איסוף תצפיות, השוואת תחזיות וסיכום מדדים.',
'MongoConsumerService.cs':'קליטת סטטוסים מ־Kafka, latest/history, אינדקסים, commit ופסילת מטמון.',
'KafkaConsumerBackService.cs':'לולאת צריכת מטא־דאטה, בחירת handler, commit וניסיון חוזר.',
'StationInformationHandler.cs':'upsert של פרטי תחנות ב־MySQL ועדכון גרסת מטמון.',
'VehicleTypesHandler.cs':'upsert של סוגי כלי רכב ב־MySQL.',
'KafkaProducerService.cs':'יצרן Kafka משותף, שליחת אצווה, אישורי מסירה ו־Dispose.',
'StationInformationService.cs':'קריאת feed פרטי תחנות, אימות ופרסום.',
'StationStatusService.cs':'קריאת זמינות, זמן קבלה, שימור null ופרסום.',
'VehicleTypesService.cs':'קריאת סוגי אופניים ופרסומם לפי מזהה הסוג.',
'StationsPollingWorker.cs':'שלוש לולאות קליטה מתוזמנות, טיפול בכשל וביטול.',
'StationDbContext.cs':'מיפוי EF לפרטי תחנות וסוגי כלים ב־stationDb.',
'OperationsDbContext.cs':'מיפוי EF לישויות תפעול ואינדקסים, כולל דוא״ל ייחודי.',
}
for root,title in [('Bike_fleet_operation_platform','א | קובצי שירות הקליטה'),('ProcessingService','ב | קובצי שירות העיבוד'),('StationApi','ג | קובצי שרת האתר'),('Shared','ד | חוזים משותפים ובדיקות')]:
 lines=['מפה זו משלימה את ההסברים בפרקים. כל שם קובץ הוא נתיב יחסי לשורש הפתרון. רשימת הפעולות נאספה מן הקוד; בנאים, overloads ומאפייני DTO מוסברים יחד עם הרכיב, ולא כיכולת עסקית עצמאית.']
 paths=list((ROOT/root).rglob('*.cs'))
 if root=='Shared': paths+=list((ROOT/'FleetTests').glob('*.cs'))
 for p in sorted(paths):
  if any(x in ('bin','obj') for x in p.parts):continue
  relative=p.relative_to(ROOT).as_posix(); src=p.read_text(encoding='utf-8-sig')
  desc=descriptions.get(p.name)
  if not desc:
   if 'Migrations' in p.parts:desc='תוצר EF של הסכימה ההיסטורית: יצירה, תיקון או snapshot. נשמר לתאימות; אינו מסך ואינו מופעל כפעולה עסקית בכל בקשה.'
   elif 'Dtos' in p.parts:desc='חוזה נתונים: מאפיינים ואילוצי ערכים למידע התחנות או לסוגי הכלים. מיפוי JSON ושמות שדות נשמרים לצורך תאימות.'
   elif 'FleetTests' in p.parts:desc='בדיקות מקומיות עם נתונים או תלויות מדומים; אינן מוזרקות לקוד הייצור. מקרי הבדיקה מפורטים בפרקי האימות.'
   else:desc='רכיב תשתית המשלים את שכבת השירות; יש לעיין בהקשר הפרק ובשמות הפעולות שלהלן.'
  methods=re.findall(r'^\s*(?:public|private|protected|internal)\s+(?:(?:static|async|override|virtual|sealed)\s+)*(?:[\w<>?,\[\].]+\s+)(\w+)\s*\(',src,re.M)
  properties=re.findall(r'public\s+[\w<>?,\[\].]+\s+(\w+)\s*\{\s*get',src)
  lines+=['קובץ: '+relative,desc]
  if methods:lines+=['פעולות: '+', '.join(dict.fromkeys(methods))+'.']
  if properties:lines+=['מאפיינים: '+', '.join(dict.fromkeys(properties))+'.']
 add('נספח '+title,'\n'.join(lines))
add('נספח ה | קובצי ממשק, תצורה וכלי עבודה','''Views/Workspace מכילה Login, Register, Dashboard, Stations, Station, Alerts, Recommendations, Metrics, Reports, Users, EditUser, Profile, Confirm ו־Error. כל מסך מופיע בפרק הצילומים עם קישור למסלול הפעולה שלו. Views/Rider מכילה Index ו־Report; Favorites היא תצוגה מסוננת של Index ולא קובץ נפרד.
Views/Shared מכילה _Layout, _StationTable ו־_Recommendations. _ViewStart קובעת את המעטפת, ו־_ViewImports מייבאת namespaces ו־Tag Helpers. site.css מגדירה את פריסת התפריט, הטבלאות, הכרטיסים, המצבים וההתאמות למסך קטן.
appsettings.json בכל שירות מכיל הגדרות ברירת מחדל: חיבורים, מקור, polling או אפשרויות ניתוח. appsettings.Development.json ו־launchSettings.json משלימים פיתוח מקומי. קובצי csproj קובעים framework וחבילות; Dockerfile בכל שירות בונה ומפרסם אותו. docker-compose.yml מחבר את השירותים, הרשת, משתני הסביבה וה־volumes.
BikeFleet.sln ו־BikeFleet.slnx מתארים את הפתרון; BikeFleet.code-workspace והגדרות .vscode מסייעים לעבודה בעורך. .editorconfig מגדיר מוסכמות קוד. index.html הוא דף הפניה מקומי פשוט ואינו אתר ה־Razor. StationApi.http הוא אוסף בקשות דוגמה, ולא endpoint עצמאי.
scripts/setup.ps1 מכין הגדרות ראשוניות; scripts/smoke-test.ps1 מבצע תרחישי בדיקה מול אתר פעיל. README, SMART_FLEET, IMPLEMENTATION_REPORT ו־CODE_CLEANUP מתעדים הפעלה, לוגיקה ואימות. תוצרי bin/obj, קובצי משתמש של IDE ו־.env אינם חלק מההסבר העסקי ואין לפרסם את הסודות שבהם.''')
add('נספח ו | מונחים ותשובות קצרות','''API: ממשק HTTP שמקבל בקשות ומחזיר JSON וקוד מצב. Controller: נקודת טיפול בבקשה, לא מסד נתונים. View: תבנית Razor שמייצרת HTML. DTO: מבנה להעברת נתונים. Entity: אובייקט שממופה למאגר יחסי. Record: טיפוס המתאים לערכים ותוצרים חישוביים.
Dependency Injection: העברת תלויות דרך container במקום בנייה מפוזרת. Scoped: מופע לאורך scope של בקשה או מחזור. Singleton: מופע יחיד בתהליך. HostedService: עבודה ברקע תחת חיי ה־host. CancellationToken: איתות לביטול פעולה אסינכרונית.
Topic: ערוץ הודעות Kafka. Partition: חלוקה של topic. Offset: מיקום הודעה במחיצה. Consumer group: קבוצת צרכנים עם מיקום צריכה. Commit: אישור מיקום לאחר טיפול. Idempotency: חזרה על פעולה בלי ליצור תוצאה כפולה לא רצויה.
Cache-aside: קריאה מהמטמון, ובחוסר קריאה ממקור ושמירה זמנית. TTL: זמן חיים. Invalidation: פסילת תוצאה ישנה. Nullable: יכולת לייצג היעדר ערך. Upsert: עדכון רשומה קיימת או יצירת חדשה. AsNoTracking: קריאה ב־EF ללא מעקב שינוי ישויות.
Claims: פרטי זהות והרשאה בתוך principal. CSRF: ניצול cookie של משתמש לפעולה מאתר זר; token מסייע למנוע זאת. Hash: ייצוג חד־כיווני לסיסמה, לא הצפנה שנפתחת כדי להשוות. MAE: ממוצע שגיאה מוחלטת. Rebalancing: העברת כלים כדי לשפר את הפיזור בין תחנות.''')
add('נספח ז | מקורות וגרסת התיעוד','''המקור הראשי לספר הוא קוד הפרויקט המקומי לאחר ניקוי הקוד. קטעי הקוד הועתקו מהקבצים המצוינים לידם. צילומי המסך נלקחו מהאתר המקומי ב־11 באוקטובר 2026, ולא נוצרו כהדמיות. המסכים משקפים גם אזהרות ושגיאות שהתרחשו בזמן התיעוד.
מקורות רשמיים לקריאה נוספת:
Citi Bike System Data: https://citibikenyc.com/system-data
מסמך הספק מפרסם feed זמינות בפורמט GBFS. הפרויקט משתמש בזמינות תחנות, ולא בנתוני נסיעות אישיים מהורדות היסטוריות.
ASP.NET Core MVC Views: https://learn.microsoft.com/en-us/aspnet/core/mvc/views/overview?view=aspnetcore-8.0
התיעוד מסביר ש־Views הן תבניות HTML עם Razor וש־partials מאפשרות שימוש חוזר בתצוגה. הספר מיישם מושגים אלה על הקבצים הקיימים בפרויקט.
Apache Kafka Documentation: https://kafka.apache.org/documentation/
תיעוד כללי למושגי הודעות וצרכנים; הגדרות הפריסה הספציפיות נמצאות בקובץ Compose של הפרויקט.
הספר אינו כולל סיסמאות, tokens או גיבובי סיסמה. דוגמאות מספריות מסומנות ככאלה. בדיקות מקומיות שעברו מתועדות בנפרד מאימות של תשתיות אמיתיות. לפני הגשה יש לקרוא את המסמך ולוודא שהמגישים מסוגלים להסביר את הבחירות ואת המגבלות במילים שלהם.''')
# Build a navigable contents list, without inventing final page numbers.
toc='תוכן עניינים\n'+'\n'.join(s['title'] for s in sections[2:])
sections.insert(2,{'title':'תוכן עניינים','body':toc.replace('תוכן עניינים\n','',1),'files':[],'image':None,'toc':True})
(OUT/'sections.json').write_text(json.dumps(sections,ensure_ascii=False,indent=2),encoding='utf-8')
print(len(sections),'sections')
