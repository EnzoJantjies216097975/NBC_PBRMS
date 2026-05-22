-- ===========================================================================
-- NBC PBRMS — 0005 storeroom
-- Make equipment bookings readable to all authenticated users (so operators can
-- see "who has what gear"), and seed a starter inventory.
-- ===========================================================================

create policy "equipment bookings readable by authenticated" on equipment_bookings
  for select to authenticated using (true);

insert into equipment (name, category, serial_number, home_location, status)
select v.name, v.category, v.serial, v.loc::location_type, 'available'::equipment_status
from (values
  ('Sony HDC Camera 1', 'Camera', 'CAM-001', 'studio_2'),
  ('Sony HDC Camera 2', 'Camera', 'CAM-002', 'studio_1'),
  ('Camera Tripod', 'Camera', 'TRP-001', null),
  ('Steadicam Rig', 'Specialised', 'STK-001', null),
  ('DJI Drone', 'Specialised', 'DRN-001', null),
  ('GoPro Hero 12', 'Specialised', 'GPR-001', null),
  ('DJI Gimbal', 'Specialised', 'GMB-001', null),
  ('Pocket Cam', 'Specialised', 'PKT-001', null),
  ('Sennheiser Wireless Mic Kit', 'Sound', 'MIC-001', 'studio_2'),
  ('Boom Mic', 'Sound', 'BOOM-001', 'studio_2'),
  ('Mic Stand', 'Sound', 'STND-001', 'studio_2'),
  ('LED Light Panel', 'Lighting', 'LGT-001', 'studio_4')
) as v(name, category, serial, loc)
on conflict (serial_number) do nothing;
