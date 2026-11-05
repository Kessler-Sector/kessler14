cmd-shipowners-desc = Lists every owned ship with its owner.
cmd-shipowners-help = Usage: {$command}
cmd-shipowners-line = {$ship} ({$uid}): owner {$owner} (profile {$profile}), price {$price}, voucher {$voucher}, bought {$date} UTC, next upkeep {$next} UTC, debt {$debt}{$impound}
cmd-shipowners-total = {$count} owned ships.

upkeep-paid = Upkeep for {$ship}: {$amount} spesos paid from your bank account.
upkeep-unpaid = You couldn't pay {$amount} spesos upkeep for {$ship}. It's owed as debt; pay it off by keeping enough money in your bank account.
upkeep-unpaid-impound = You still owe {$amount} spesos upkeep for {$ship}. It's overdue and the ship will be impounded.

cmd-upkeeprun-desc = Bills ship upkeep now. Optionally pretends that many hours have passed, for testing.
cmd-upkeeprun-help = Usage: {$command} [hours ahead]
cmd-upkeeprun-bad-hours = Hours must be a number.
cmd-upkeeprun-done = Upkeep billed as of {$time} UTC.
cmd-shipowners-impound = , due for impounding

ship-storage-impounded = {$ship} has been impounded for {$debt} spesos of unpaid upkeep. Pay it at a shipyard console to recall the ship.
ship-storage-inactive = {$ship} has been moved into storage while you were away. Recall it at any shipyard console.
ship-storage-admin = {$ship} has been moved into storage by an admin. Recall it at any shipyard console.
ship-storage-verb-recall = Recall {$ship}
ship-storage-verb-recall-debt = Recall {$ship} (pay {$debt} spesos)
ship-storage-recall-no-id = Insert your ID card first.
ship-storage-recall-no-station = This console isn't part of a station.
ship-storage-recall-debt = You need {$debt} spesos in your bank account to pay the upkeep owed.
ship-storage-recall-failed = The ship couldn't be recalled. Contact an admin.
ship-storage-recalled = {$ship} is on its way to the dock.

cmd-storedships-desc = Lists the ships in storage.
cmd-storedships-help = Usage: {$command}
cmd-storedships-line = {$id}: {$ship}, owner {$owner}, {$reason}, debt {$debt}, stored {$date} UTC
cmd-storedships-total = {$count} stored ships.
cmd-shipstore-desc = Moves an owned ship into storage now.
cmd-shipstore-help = Usage: {$command} <ship entity>
cmd-shipstore-done = Ship stored.
cmd-shipstore-failed = Couldn't store the ship: {$problem}
cmd-shiprecall-desc = Brings a stored ship back, docked at the station you're on.
cmd-shiprecall-help = Usage: {$command} <stored ship id>
cmd-shiprecall-done = Recalled as {$ship}.
cmd-shiprecall-failed = Couldn't recall the ship: {$problem}
