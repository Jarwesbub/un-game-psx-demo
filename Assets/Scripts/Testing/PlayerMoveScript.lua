-- Math calculations:
local zero = FixedPoint.new(0)
local one = FixedPoint.new(1)
local two = FixedPoint.new(2)
local invSqrt2 = FixedPoint.new(7071) / 10000 -- ≈ 0.7071

local directionIndex = 0 -- 0=forward, 4=backward
local step = one / 256

local isCoolDown = false
local coolDownSteps = zero
local coolDownTime = FixedPoint.new(1) / 1000

local player = Entity.Find("PlayerModel")

local directions = {
    Vec3.new(zero, zero, one),                    -- [0] Forward (N)
    Vec3.new(one, zero, one),                     -- [1] Forward-Right (NE)
    Vec3.new(one, zero, zero),                    -- [2] Right (E)
    Vec3.new(one, zero, -one),                    -- [3] Back-Right (SE)
    Vec3.new(zero, zero, -one),                   -- [4] Back (S)
    Vec3.new(-one, zero, -one),                   -- [5] Back-Left (SW)
    Vec3.new(-one, zero, zero),                   -- [6] Left (W)
    Vec3.new(-one, zero, one),                    -- [7] Forward-Left (NW)
}

local rotations = {
    zero,           -- [0] 0° (N)
    one / 4,        -- [1] 0.25 => 45° (NE)
    one / 2,        -- [2] 0.5 => 90° (E)
    one * 3 / 4,    -- [3] 0.75 => 135° (SE)
    one,            -- [4] 1.0 => 180° (S)
    one * 5 / 4,    -- [5] 1.25 => 225° (SW)
    one * 3 / 2,    -- [6] 1.5 => 270° (W)
    one * 7 / 4,    -- [7] 1.75 => 315° (NW)
}

function onCreate(self)
    player = Entity.Find("PlayerModel")
    SkinnedAnim.Play("PlayerModel", "idle", { loop = true })

end

function onUpdate(self, dt)
    -- Hotfix.
    if not player then
        player = Entity.Find("PlayerModel")
    end
    local pos = player.position

    if isCoolDown then
        coolDownSteps = coolDownSteps - (one * dt)
        if coolDownSteps <= zero then
            coolDownSteps = zero
            isCoolDown = false
        end
    else
        playerTurn()
        Entity.SetRotationY(player, rotations[directionIndex + 1]) -- Rotate object.
        coolDownSteps = coolDownTime
        isCoolDown = true
    end

    

    -- Normalize diagonal movement
    local forward = directions[directionIndex + 1]
    if directionIndex % 2 == 1 then -- Diagonal
        forward = Vec3.mul(forward, invSqrt2)
    end

    if Input.IsHeld(Input.UP) then
        local move = Vec3.mul(forward, step)
        Entity.SetPosition(player, Vec3.add(pos, move))
    elseif Input.IsHeld(Input.DOWN) then
        local move = Vec3.mul(forward, -(step / 2)) -- Move slower when walking backwards.
        Entity.SetPosition(player, Vec3.add(pos, move))
    end
end


function playerTurn()
    if Input.IsHeld(Input.LEFT) then
        directionIndex = directionIndex - 1
        if directionIndex < 0 then directionIndex = 7 end
    elseif Input.IsHeld(Input.RIGHT) then
        directionIndex = directionIndex + 1
        if directionIndex > 7 then directionIndex = 0 end
    end
end

function onButtonPress(self, button)

    -- Player animations:
    if button == Input.UP or button == Input.DOWN then
        SkinnedAnim.Play("PlayerModel", "walk", { loop = true })
    end
end

function onButtonRelease(self, button)
    -- Player animations:
    if button == Input.UP or button == Input.DOWN then
        SkinnedAnim.Play("PlayerModel", "idle", { loop = true })
    end
end