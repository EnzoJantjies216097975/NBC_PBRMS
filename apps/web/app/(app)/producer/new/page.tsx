import { requireRole } from '@/lib/auth';
import {
  LOCATION_TYPES,
  LOCATION_TYPE_LABELS,
  SPECIALISED_EQUIPMENT,
  SPECIALISED_EQUIPMENT_LABELS,
} from '@nbc/shared';
import { createClient } from '@/lib/supabase/server';
import { createBooking } from './actions';

export default async function NewBookingPage({
  searchParams,
}: {
  searchParams: Promise<{ error?: string }>;
}) {
  await requireRole(['producer', 'executive_producer', 'admin']);
  const { error } = await searchParams;

  const supabase = await createClient();
  const { data: productions } = await supabase
    .from('productions')
    .select('id, name, default_location')
    .eq('is_active', true)
    .order('name');

  return (
    <div className="mx-auto max-w-3xl">
      <h1 className="mb-1 text-2xl font-semibold">New production booking</h1>
      <p className="mb-6 text-sm text-slate-600">
        Raise a booking request to your Executive Producer. They validate it and assign the channel
        before it goes to TV Operations for crewing.
      </p>
      {error && <p className="mb-4 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}

      <form action={createBooking} className="space-y-6">
        <section className="card space-y-4">
          <h2 className="font-medium">Production</h2>
          <div>
            <label className="label" htmlFor="title">
              Production name
            </label>
            <input className="input" id="title" name="title" required placeholder="e.g. Talk of the Nation / Ad-hoc event" />
          </div>
          <div>
            <label className="label" htmlFor="production_id">
              Link to existing show (optional)
            </label>
            <select className="input" id="production_id" name="production_id" defaultValue="">
              <option value="">— Ad hoc / not in catalog —</option>
              {(productions ?? []).map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name}
                </option>
              ))}
            </select>
          </div>
        </section>

        <section className="card space-y-4">
          <h2 className="font-medium">Location</h2>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="label" htmlFor="location_type">
                Where
              </label>
              <select className="input" id="location_type" name="location_type" required defaultValue="studio_2">
                {LOCATION_TYPES.map((lt) => (
                  <option key={lt} value={lt}>
                    {LOCATION_TYPE_LABELS[lt]}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label className="label" htmlFor="venue">
                Venue (for location / OB / fly-away)
              </label>
              <input className="input" id="venue" name="venue" placeholder="Address or venue name" />
            </div>
          </div>
          <div className="flex flex-wrap gap-x-6 gap-y-2 text-sm">
            <label className="flex items-center gap-2"><input type="checkbox" name="needs_ob_van" /> OB Van</label>
            <label className="flex items-center gap-2"><input type="checkbox" name="is_streaming" /> Streaming</label>
            <label className="flex items-center gap-2"><input type="checkbox" name="uses_kiloview" /> Kiloview</label>
            <label className="flex items-center gap-2"><input type="checkbox" name="is_once_off" /> Once-off event</label>
            <label className="flex items-center gap-2"><input type="checkbox" name="is_ad_hoc" /> Ad hoc</label>
            <label className="flex items-center gap-2"><input type="checkbox" name="requires_car_booking" /> Crew need a vehicle</label>
          </div>
        </section>

        <section className="card space-y-4">
          <h2 className="font-medium">Schedule</h2>
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
            <div>
              <label className="label" htmlFor="call_date">Crew call date</label>
              <input className="input" id="call_date" name="call_date" type="date" required />
            </div>
            <div>
              <label className="label" htmlFor="call_time">Crew call time</label>
              <input className="input" id="call_time" name="call_time" type="time" required />
            </div>
            <div>
              <label className="label" htmlFor="end_time">Expected end time</label>
              <input className="input" id="end_time" name="end_time" type="time" />
            </div>
            <div>
              <label className="label" htmlFor="air_date">Air date</label>
              <input className="input" id="air_date" name="air_date" type="date" />
            </div>
            <div>
              <label className="label" htmlFor="air_start">Air start</label>
              <input className="input" id="air_start" name="air_start" type="time" />
            </div>
            <div>
              <label className="label" htmlFor="air_end">Air end</label>
              <input className="input" id="air_end" name="air_end" type="time" />
            </div>
          </div>
          <div className="w-40">
            <label className="label" htmlFor="duration_minutes">Length (minutes)</label>
            <input className="input" id="duration_minutes" name="duration_minutes" type="number" min={0} />
          </div>
        </section>

        <section className="card space-y-3">
          <h2 className="font-medium">Specialised equipment</h2>
          <div className="flex flex-wrap gap-x-6 gap-y-2 text-sm">
            {SPECIALISED_EQUIPMENT.map((eq) => (
              <label key={eq} className="flex items-center gap-2">
                <input type="checkbox" name="specialised_equipment" value={eq} />
                {SPECIALISED_EQUIPMENT_LABELS[eq]}
              </label>
            ))}
          </div>
        </section>

        <section className="card space-y-3">
          <h2 className="font-medium">Notes for crew &amp; Exec Producer</h2>
          <textarea className="input min-h-24" name="notes" placeholder="Any relevant information, comments, requirements…" />
        </section>

        <div className="flex gap-3">
          <button className="btn-primary" type="submit" name="intent" value="submit">
            Submit to Exec Producer
          </button>
          <button className="btn-ghost" type="submit" name="intent" value="draft">
            Save draft
          </button>
        </div>
      </form>
    </div>
  );
}
