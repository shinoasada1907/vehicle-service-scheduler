## ADDED Requirements

The scenarios V1–V8, A1–A4, R1–R10, C1–C5 and X1–X2 are the test scenarios approved by the owner on 2026-11-03. Tests cover exactly these scenarios; any additional test needs the owner's approval. Examples use the London dealership (`Europe/London`, Monday–Friday 08:00–18:00) on Monday 2030-01-07 and a 60-minute oil change, with two general bays and three qualified technicians.

### Requirement: List free start times for one local day
The system SHALL return, for a dealership, a service type and a date in the dealership's local calendar, every start time on a 15-minute boundary from opening time to closing time minus the service duration for which a compatible bay and a qualified technician are free for the whole half-open range, using the same rule as booking. Times SHALL be returned in UTC with the dealership's time zone. Start times that are not after the current time SHALL be left out.

#### Scenario: V1 — Empty day
- **WHEN** availability is requested for a day with no appointments
- **THEN** the system responds 200 with start times every 15 minutes from 08:00 to 17:00 local time, in UTC, with the dealership's time zone

#### Scenario: V2 — Busy resources for part of a range
- **WHEN** both general bays are booked from 09:00 to 10:00
- **THEN** the start times 08:15 to 09:45 are not listed, while 08:00 and 10:00 are

#### Scenario: V3 — Cancelled appointments do not block
- **WHEN** the appointments occupying a time have been cancelled
- **THEN** that time is listed again

#### Scenario: V4 — Today
- **WHEN** availability is requested for the current local date
- **THEN** start times that are not after the current time are not listed

#### Scenario: V5 — Past date or closed day
- **WHEN** availability is requested for a past date or for a Sunday
- **THEN** the system responds 200 with an empty list

#### Scenario: V6 — Summer time
- **WHEN** availability is requested for 2030-07-01
- **THEN** the first start time is `2030-07-01T07:00:00Z` (08:00 British Summer Time)

#### Scenario: V7 — Service not offered
- **WHEN** availability for a wheel alignment is requested at the Saigon dealership, which has no alignment bay
- **THEN** the system responds 422 with type `service-not-offered`

#### Scenario: V8 — Unknown references and malformed queries
- **WHEN** the dealership or service type does not exist, or `date` is missing or not in `yyyy-MM-dd` format
- **THEN** the system responds 404 with type `not-found`, or 400 with type `validation` and an error for the field

### Requirement: Suggest alternative times for an existing appointment
The system SHALL list free start times for an existing confirmed appointment on a requested local date (by default the appointment's own date), treating the appointment's own bay, technician and vehicle time as free and leaving out times that overlap another confirmed appointment of the same vehicle. It SHALL apply the change rules of this capability.

#### Scenario: A1 — Own slot counts as free
- **WHEN** alternatives are requested for an appointment from 09:00 to 10:00 while the other bay is also busy from 09:00 to 10:00
- **THEN** 09:30 is listed even though it overlaps the appointment itself

#### Scenario: A2 — The vehicle's other appointments
- **WHEN** the same vehicle has another appointment the same day from 14:00 to 15:00
- **THEN** start times overlapping 14:00–15:00 are not listed

#### Scenario: A3 — Another date
- **WHEN** alternatives are requested with `date` set to another day
- **THEN** the free start times of that day are listed

#### Scenario: A4 — Appointment that cannot change
- **WHEN** alternatives are requested for a cancelled appointment, for an appointment starting within the cut-off, or for an unknown id
- **THEN** the system responds 422 with type `appointment-cancelled`, 422 with type `change-window-closed`, or 404 with type `not-found`

### Requirement: Reschedule an appointment atomically
The system SHALL move a confirmed appointment to a new start time in one step, keeping its id, dealership, service type, customer and vehicle and choosing the bay and technician again with the booking rule. The new range SHALL satisfy the booking rules. If the move cannot be made, the appointment SHALL stay exactly as it was. The database SHALL keep preventing overlaps for bays, technicians and vehicles, including when a reschedule and a booking run concurrently.

#### Scenario: R1 — Move to a free time
- **WHEN** an appointment is moved to a free start time
- **THEN** the system responds 200 with the same id, the new start and end times and the chosen bay and technician

#### Scenario: R2 — Move overlapping the old time
- **WHEN** an appointment from 09:00 to 10:00 is moved to 09:30
- **THEN** the move succeeds

#### Scenario: R3 — No capacity at the new time
- **WHEN** no compatible bay or no qualified technician is free at the new time
- **THEN** the system responds 409 with type `no-capacity` and a `shortage`, and reading the appointment returns its old time, bay and technician

#### Scenario: R4 — Vehicle already booked at the new time
- **WHEN** the vehicle has another confirmed appointment overlapping the new time
- **THEN** the system responds 409 with type `vehicle-already-booked` and the appointment is unchanged

#### Scenario: R5 — New time breaks a booking rule
- **WHEN** the new start time is in the past, off the 15-minute grid or outside opening hours
- **THEN** the system responds 422 with type `invalid-start-time` or `outside-opening-hours`

#### Scenario: R6 — Change cut-off
- **WHEN** the appointment starts 2 hours 15 minutes from now
- **THEN** it can be moved
- **WHEN** the appointment starts exactly 2 hours from now, sooner, or has started
- **THEN** the system responds 422 with type `change-window-closed`

#### Scenario: R7 — Cancelled appointment
- **WHEN** a cancelled appointment is rescheduled
- **THEN** the system responds 422 with type `appointment-cancelled`

#### Scenario: R8 — Same time
- **WHEN** the new start time equals the current one
- **THEN** the system responds 200 and nothing changes

#### Scenario: R9 — Old time is released
- **WHEN** an appointment has been moved
- **THEN** another vehicle can book its old time

#### Scenario: R10 — Reschedule and booking race for the last free time
- **WHEN** a reschedule and a booking for the same last free time run in parallel
- **THEN** exactly one succeeds and the database holds no overlapping confirmed appointments

### Requirement: Cancel an appointment
The system SHALL let a confirmed appointment be cancelled while it starts more than the change cut-off from now. The status SHALL become `Cancelled` and the cancellation time SHALL be stored; the bay, technician and vehicle SHALL be free for that range at once. Cancelling an already cancelled appointment SHALL return its current state.

#### Scenario: C1 — Cancel in time
- **WHEN** an appointment starting more than 2 hours from now is cancelled
- **THEN** the system responds 200 with status `Cancelled` and `cancelledAt`, and reading it back returns the same

#### Scenario: C2 — The slot is free again
- **WHEN** an appointment has been cancelled
- **THEN** its time is listed by the availability lookup and another vehicle can book it

#### Scenario: C3 — Cancel twice
- **WHEN** a cancelled appointment is cancelled again
- **THEN** the system responds 200 with the state after the first cancellation

#### Scenario: C4 — Too late to cancel
- **WHEN** the appointment starts exactly 2 hours from now, sooner, or has started
- **THEN** the system responds 422 with type `change-window-closed`

#### Scenario: C5 — Unknown appointment
- **WHEN** an unknown appointment id is cancelled
- **THEN** the system responds 404 with type `not-found`

### Requirement: Configurable change cut-off and error format
The change cut-off SHALL be read from configuration (`Scheduling:ChangeCutoff`, default 2 hours). Every error of this capability SHALL use the project's Problem Details format.

#### Scenario: X1 — Cut-off from configuration
- **WHEN** the cut-off is configured as 1 hour
- **THEN** an appointment starting 1 hour 15 minutes from now can be cancelled

#### Scenario: X2 — Error format
- **WHEN** any request of this capability fails
- **THEN** the response is Problem Details with a type `urn:vehicle-service-scheduler:problem:<slug>`, a `title`, the `status`, a `detail` and a `traceId`
