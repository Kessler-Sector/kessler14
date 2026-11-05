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
