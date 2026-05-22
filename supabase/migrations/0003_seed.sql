-- ===========================================================================
-- NBC PBRMS — 0003 seed (reference data)
-- Departments, shifts, skills, and the recurring production catalog.
-- Mirrors packages/shared/src/constants.ts. Re-runnable (guarded inserts).
-- ===========================================================================

-- ---- Departments: operations (crew) ----
insert into departments (name, kind)
select v.name, 'operations'::department_kind
from (values
  ('Camera'), ('Lighting'), ('Sound'), ('News Camera'),
  ('Production Officers'), ('Technicians'), ('Final Control Centre'), ('OB Operations')
) as v(name)
where not exists (select 1 from departments d where d.name = v.name and d.parent_id is null);

-- ---- Departments: content (editorial) ----
insert into departments (name, kind)
select v.name, 'content'::department_kind
from (values
  ('Content Hub'), ('News'), ('Current Affairs'), ('New Business'), ('Sports')
) as v(name)
where not exists (select 1 from departments d where d.name = v.name and d.parent_id is null);

-- ---- Content Hub sub-divisions ----
insert into departments (name, kind, parent_id)
select v.name, 'content'::department_kind, (select id from departments where name = 'Content Hub' and parent_id is null)
from (values
  ('Education'), ('Entertainment'), ('Documentaries'), ('AdHoc'),
  ('Drama'), ('Reality'), ('Social Media')
) as v(name)
where not exists (
  select 1 from departments d
  where d.name = v.name
    and d.parent_id = (select id from departments where name = 'Content Hub' and parent_id is null)
);

-- ---- Shifts ----
insert into shifts (code, name, start_time, end_time, as_per_booking, is_working) values
  ('FM',  'Floor Managing',         '05:00', '13:00', false, true),
  ('GM',  'Good Morning',           '05:00', '13:00', false, true),
  ('ST2', 'Studio 2',               '14:00', '23:00', false, true),
  ('ST4', 'Studio 4',               '08:00', '17:00', false, true),
  ('N',   'News',                   '13:00', '21:00', false, true),
  ('APP', 'Audio Post Production',   null,    null,    true,  true),
  ('TR',  'Trips',                  null,    null,    true,  true),
  ('A1',  'As Per Booking 1',       null,    null,    true,  true),
  ('A2',  'As Per Booking 2',       null,    null,    true,  true),
  ('A3',  'As Per Booking 3',       null,    null,    true,  true),
  ('A4',  'As Per Booking 4',       null,    null,    true,  true),
  ('A5',  'As Per Booking 5',       null,    null,    true,  true),
  ('A6',  'As Per Booking 6',       null,    null,    true,  true),
  ('O',   'Off',                    null,    null,    false, false),
  ('PH',  'Public Holiday',         null,    null,    false, false),
  ('L',   'Leave',                  null,    null,    false, false)
on conflict (code) do nothing;

-- ---- Skills / specialised gear capability ----
insert into skills (name, description)
select v.name, v.descr from (values
  ('Steadicam', 'Operate a Steadicam stabiliser rig'),
  ('Gimbal', 'Operate a motorised gimbal'),
  ('GoPro', 'Action camera setup and operation'),
  ('Drone', 'Licensed drone / aerial operator'),
  ('Pocket Cam', 'Compact / pocket camera operation'),
  ('Jib', 'Operate a camera jib / crane'),
  ('Wireless Mic Kit', 'Wireless microphone rigging')
) as v(name, descr)
where not exists (select 1 from skills s where s.name = v.name);

-- ---- Production catalog ----
insert into productions (name, kind, default_location, is_live, content_department_id)
select v.name, v.kind, v.loc::location_type, v.live,
       (select id from departments d where d.name = v.dept and d.parent_id is null)
from (values
  -- daily (Mon–Fri)
  ('Good Morning Namibia', 'daily', 'studio_2', true,  null),
  ('Namibia Connects',     'daily', 'studio_1', false, null),
  ('1 o''Clock News',      'daily', 'studio_1', true,  'News'),
  ('Eye on SADC',          'daily', 'studio_1', false, null),
  ('Indigenous News',      'daily', 'studio_1', false, 'News'),
  ('Daily Round-Up',       'daily', 'studio_2', false, null),
  ('8 o''Clock News',      'daily', 'studio_1', true,  'News'),
  ('Sport News',           'daily', 'studio_2', false, 'Sports'),
  -- weekend
  ('Sports Breakfast Show',     'weekend', 'studio_2', false, 'Sports'),
  ('Morning News Highlights',   'weekend', 'studio_1', false, 'News'),
  ('4 o''Clock News Highlights','weekend', 'studio_1', false, 'News'),
  ('6 o''Clock News Highlights','weekend', 'studio_1', false, 'News'),
  ('Wheels of Justice',         'weekend', 'studio_2', false, null),
  -- flagship live
  ('Talk of the Nation', 'flagship', 'studio_2', true, null),
  ('Business Today',     'flagship', 'studio_2', true, null),
  ('Tupopyeni',          'flagship', 'studio_2', true, null),
  ('Whatagwan',          'flagship', 'studio_4', true, null),
  ('Situation Kritical', 'flagship', 'studio_2', true, null),
  ('Soccer Pitch',       'flagship', 'studio_2', true, 'Sports'),
  -- recorded: Current Affairs
  ('Inside the Chambers',   'recorded', 'studio_2', false, 'Current Affairs'),
  ('One on One',            'recorded', 'studio_2', false, 'Current Affairs'),
  ('Public Service Corner', 'recorded', 'studio_2', false, 'Current Affairs'),
  -- recorded: Content Hub
  ('Toucy T Show',   'recorded', 'studio_2', false, 'Content Hub'),
  ('Man Unfiltered', 'recorded', 'studio_2', false, 'Content Hub'),
  ('New Season',     'recorded', null,       false, 'Content Hub'),
  ('Unrooted',       'recorded', null,       false, 'Content Hub'),
  ('@AM Saturday',   'recorded', 'studio_4', false, 'Content Hub'),
  ('@AM Sunday',     'recorded', 'studio_4', false, 'Content Hub'),
  ('On My Playlist', 'recorded', 'studio_2', false, 'Content Hub'),
  ('Sunshine Club',  'recorded', null,       false, 'Content Hub'),
  ('Just Teenz',     'recorded', null,       false, 'Content Hub'),
  -- recorded: Sports
  ('Sports Essentials', 'recorded', 'studio_2', false, 'Sports'),
  ('Absolute Rugby',    'recorded', 'studio_2', false, 'Sports'),
  -- location shoots
  ('Tutaleni',          'location', 'on_location', false, null),
  ('Legends of Change', 'location', 'on_location', false, null),
  ('Miss Namibia',      'location', 'on_location', false, null),
  ('In The Community',  'location', 'on_location', false, null),
  ('Sports Uncovered',  'location', 'on_location', false, 'Sports'),
  ('Twin Turbo',        'location', 'on_location', false, null),
  ('She Means Business','location', 'on_location', false, null),
  ('Whatalifestyle',    'location', 'on_location', false, null)
) as v(name, kind, loc, live, dept)
where not exists (select 1 from productions p where p.name = v.name);
