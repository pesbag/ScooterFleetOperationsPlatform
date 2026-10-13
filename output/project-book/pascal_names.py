from pathlib import Path
import re,json
mapping={'Station_id':'StationId','Num_bikes_available':'NumBikesAvailable','Nun_vehicles_available':'NumVehiclesAvailable','Num_docs_available':'NumDocksAvailable','Is_renting':'IsRenting','Is_returning':'IsReturning','Last_reported':'LastReported','Vehicle_type_id':'VehicleTypeId','Form_factor':'FormFactor','propulsion_type':'PropulsionType','GetFiletrDto':'GetFilterDto'}
pattern=re.compile(r'//[^\n]*|/\*[\s\S]*?\*/|\$?@?"(?:\\.|""|[^"\\])*"|\b[A-Za-z_][A-Za-z_0-9]*\b')
def rename(m):
 t=m.group()
 if t.startswith('$"'):
  return re.sub(r'\{[^{}]*\}',lambda x:re.sub(r'\b\w+\b',lambda y:mapping.get(y.group(),y.group()),x.group()),t)
 return mapping.get(t,t)
for root in ['BikeFleetOperationPlatform','ProcessingService','StationApi','FleetTests']:
 for p in Path(root).rglob('*.cs'):
  if any(x in p.parts for x in ['obj','bin','Migrations']):continue
  s=p.read_text(encoding='utf-8-sig'); old=s
  if 'Dtos' in p.parts:
   lines=s.splitlines(True);result=[]
   for line in lines:
    found=re.search(r'public\s+[\w?]+\s+(\w+)\s*\{',line)
    if found and found[1] in mapping:
     name=found[1]; prior=''.join(result[-4:])
     if 'JsonPropertyName' not in prior or re.search(r'public\s',prior.split('JsonPropertyName')[-1]):
      wire=name[0].lower()+name[1:] if root=='StationApi' else name
      result.append('    [System.Text.Json.Serialization.JsonPropertyName("'+wire+'") ]\n')
     if p.name in ['StationInformationDto.cs','VehicleTypesDto.cs'] and root!='BikeFleetOperationPlatform':
      result.append('    [System.ComponentModel.DataAnnotations.Schema.Column("'+name+'") ]\n')
    result.append(line)
   s=''.join(result)
  s=pattern.sub(rename,s)
  # DbSet names are public; local variables retain camelCase.
  s=re.sub(r'(?<=\.)stationInformation\b','StationInformation',s)
  s=re.sub(r'(?<=\.)vehicleTypes\b','VehicleTypes',s)
  s=s.replace('> stationInformation {','> StationInformation {').replace('> vehicleTypes {','> VehicleTypes {')
  if p.name=='StationDbContext.cs':
   s=s.replace('.HasKey(e => e.StationId);','.ToTable("stationInformation").HasKey(e => e.StationId);').replace('.HasKey(e => e.VehicleTypeId);','.ToTable("vehicleTypes").HasKey(e => e.VehicleTypeId);')
  if s!=old:p.write_text(s,encoding='utf8')
p=Path('StationApi/Dtos/GetFiletrDto.cs');p.rename(p.with_name('GetFilterDto.cs'))
Path('output/project-book/name-map.json').write_text(json.dumps(mapping),encoding='utf8')
