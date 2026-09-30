# WMS End-to-End Test Script
$BASE    = "http://localhost:5000/api"
$DB      = "localhost\SQLEXPRESS"
$DB_NAME = "SynteraWMS_Enterprise"
$global:OK   = 0
$global:FAIL = 0

function Log-OK   { param($msg) Write-Host "  [OK]  $msg" -ForegroundColor Green;  $global:OK++ }
function Log-FAIL { param($msg) Write-Host "  [FAIL] $msg" -ForegroundColor Red;   $global:FAIL++ }
function Log-INFO { param($msg) Write-Host "        $msg"  -ForegroundColor Cyan }
function Log-HEAD { param($msg) Write-Host "`n=== $msg ===" -ForegroundColor Yellow }

# All endpoints return { success, data }. This unwraps .data automatically.
function Invoke-API {
    param($Method, $Path, $Body)
    $headers = @{ "Content-Type" = "application/json" }
    $uri = "$BASE$Path"
    try {
        if ($Body) {
            $json = $Body | ConvertTo-Json -Depth 10
            $resp = Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -Body $json -ErrorAction Stop
        } else {
            $resp = Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -ErrorAction Stop
        }
        # All controllers return { success, data }
        if ($resp.PSObject.Properties.Name -contains "data") { return $resp.data }
        return $resp
    } catch {
        $status = $_.Exception.Response.StatusCode.value__
        $detail = $_.ErrorDetails.Message
        throw "HTTP $status - $detail"
    }
}

function Run-SQL {
    param($Query)
    $result = sqlcmd -S $DB -d $DB_NAME -Q $Query -E -h -1 -W 2>&1
    return ($result | Where-Object { $_ -notmatch "^\s*$" } | Select-Object -First 1)
}

# ============================================================
Log-HEAD "STEP 1 - SEED MASTER DATA VIA SQL"
# ============================================================

Run-SQL "IF NOT EXISTS (SELECT 1 FROM Categories WHERE CategoryCode='CAT-001') INSERT INTO Categories (CategoryCode,CategoryName,CreatedAt) VALUES ('CAT-001','Electronics',GETUTCDATE());" | Out-Null
Run-SQL "IF NOT EXISTS (SELECT 1 FROM Categories WHERE CategoryCode='CAT-002') INSERT INTO Categories (CategoryCode,CategoryName,CreatedAt) VALUES ('CAT-002','FMCG',GETUTCDATE());" | Out-Null
Run-SQL "IF NOT EXISTS (SELECT 1 FROM Categories WHERE CategoryCode='CAT-003') INSERT INTO Categories (CategoryCode,CategoryName,CreatedAt) VALUES ('CAT-003','Raw Material',GETUTCDATE());" | Out-Null
Run-SQL "IF NOT EXISTS (SELECT 1 FROM Categories WHERE CategoryCode='CAT-004') INSERT INTO Categories (CategoryCode,CategoryName,CreatedAt) VALUES ('CAT-004','Packaging',GETUTCDATE());" | Out-Null
Log-OK "Categories seeded (4)"

Run-SQL "IF NOT EXISTS (SELECT 1 FROM UOM WHERE UOMCode='PCS') INSERT INTO UOM (UOMCode,UOMName) VALUES ('PCS','Pieces');" | Out-Null
Run-SQL "IF NOT EXISTS (SELECT 1 FROM UOM WHERE UOMCode='KG')  INSERT INTO UOM (UOMCode,UOMName) VALUES ('KG','Kilogram');" | Out-Null
Run-SQL "IF NOT EXISTS (SELECT 1 FROM UOM WHERE UOMCode='BOX') INSERT INTO UOM (UOMCode,UOMName) VALUES ('BOX','Box');" | Out-Null
Run-SQL "IF NOT EXISTS (SELECT 1 FROM UOM WHERE UOMCode='CTN') INSERT INTO UOM (UOMCode,UOMName) VALUES ('CTN','Carton');" | Out-Null
Run-SQL "IF NOT EXISTS (SELECT 1 FROM UOM WHERE UOMCode='LTR') INSERT INTO UOM (UOMCode,UOMName) VALUES ('LTR','Liter');" | Out-Null
Log-OK "UOM seeded (5)"

$binSQL = @"
IF NOT EXISTS (SELECT 1 FROM BinLocations WHERE BinCode='A-01-01')
BEGIN
  INSERT INTO BinLocations (WarehouseId,BinCode,Zone,Rack,LevelNo,CapacityQty,IsActive,CreatedAt) VALUES
    (1,'A-01-01','A','R01','L1',50,1,GETUTCDATE()),
    (1,'A-01-02','A','R01','L2',50,1,GETUTCDATE()),
    (1,'A-02-01','A','R02','L1',50,1,GETUTCDATE()),
    (1,'A-02-02','A','R02','L2',50,1,GETUTCDATE()),
    (1,'A-03-01','A','R03','L1',50,1,GETUTCDATE()),
    (1,'B-01-01','B','R01','L1',50,1,GETUTCDATE()),
    (1,'B-01-02','B','R01','L2',50,1,GETUTCDATE()),
    (1,'B-02-01','B','R02','L1',50,1,GETUTCDATE()),
    (1,'B-02-02','B','R02','L2',50,1,GETUTCDATE()),
    (1,'C-01-01','C','R01','L1',50,1,GETUTCDATE()),
    (1,'C-01-02','C','R01','L2',50,1,GETUTCDATE()),
    (1,'C-02-01','C','R02','L1',50,1,GETUTCDATE());
END
"@
Run-SQL $binSQL | Out-Null
Log-OK "Bin Locations seeded (12 bins: Zone A/B/C)"

$skuSQL = @"
DECLARE @catElec INT = (SELECT Id FROM Categories WHERE CategoryCode='CAT-001');
DECLARE @catFMCG INT = (SELECT Id FROM Categories WHERE CategoryCode='CAT-002');
DECLARE @catRaw  INT = (SELECT Id FROM Categories WHERE CategoryCode='CAT-003');
DECLARE @catPack INT = (SELECT Id FROM Categories WHERE CategoryCode='CAT-004');
DECLARE @uomPCS  INT = (SELECT Id FROM UOM WHERE UOMCode='PCS');
DECLARE @uomKG   INT = (SELECT Id FROM UOM WHERE UOMCode='KG');
DECLARE @uomCTN  INT = (SELECT Id FROM UOM WHERE UOMCode='CTN');
DECLARE @uomLTR  INT = (SELECT Id FROM UOM WHERE UOMCode='LTR');
IF NOT EXISTS (SELECT 1 FROM MasterSKU WHERE SKUCode='SKU-LAPTOP-001')
  INSERT INTO MasterSKU (SKUCode,SKUName,CategoryId,UOMId,Barcode,Qty,MinStock,MaxStock,Status,CreatedAt,UpdatedAt)
  VALUES ('SKU-LAPTOP-001','Laptop Dell XPS 15 Inch',@catElec,@uomPCS,'8901234560001',0,5,50,'Active',GETUTCDATE(),GETUTCDATE());
IF NOT EXISTS (SELECT 1 FROM MasterSKU WHERE SKUCode='SKU-MOUSE-002')
  INSERT INTO MasterSKU (SKUCode,SKUName,CategoryId,UOMId,Barcode,Qty,MinStock,MaxStock,Status,CreatedAt,UpdatedAt)
  VALUES ('SKU-MOUSE-002','Mouse Wireless Logitech M720',@catElec,@uomPCS,'8901234560002',0,10,200,'Active',GETUTCDATE(),GETUTCDATE());
IF NOT EXISTS (SELECT 1 FROM MasterSKU WHERE SKUCode='SKU-KEYBD-003')
  INSERT INTO MasterSKU (SKUCode,SKUName,CategoryId,UOMId,Barcode,Qty,MinStock,MaxStock,Status,CreatedAt,UpdatedAt)
  VALUES ('SKU-KEYBD-003','Keyboard Mechanical Rexus K9',@catElec,@uomPCS,'8901234560003',0,10,150,'Active',GETUTCDATE(),GETUTCDATE());
IF NOT EXISTS (SELECT 1 FROM MasterSKU WHERE SKUCode='SKU-MONIT-004')
  INSERT INTO MasterSKU (SKUCode,SKUName,CategoryId,UOMId,Barcode,Qty,MinStock,MaxStock,Status,CreatedAt,UpdatedAt)
  VALUES ('SKU-MONIT-004','Monitor LG 27 Inch 4K',@catElec,@uomPCS,'8901234560004',0,5,40,'Active',GETUTCDATE(),GETUTCDATE());
IF NOT EXISTS (SELECT 1 FROM MasterSKU WHERE SKUCode='SKU-MINYAK-005')
  INSERT INTO MasterSKU (SKUCode,SKUName,CategoryId,UOMId,Barcode,Qty,MinStock,MaxStock,Status,CreatedAt,UpdatedAt)
  VALUES ('SKU-MINYAK-005','Minyak Goreng Bimoli 2 Liter',@catFMCG,@uomLTR,'8901234560005',0,50,500,'Active',GETUTCDATE(),GETUTCDATE());
IF NOT EXISTS (SELECT 1 FROM MasterSKU WHERE SKUCode='SKU-GULA-006')
  INSERT INTO MasterSKU (SKUCode,SKUName,CategoryId,UOMId,Barcode,Qty,MinStock,MaxStock,Status,CreatedAt,UpdatedAt)
  VALUES ('SKU-GULA-006','Gula Pasir Premium 1 KG',@catFMCG,@uomKG,'8901234560006',0,50,500,'Active',GETUTCDATE(),GETUTCDATE());
IF NOT EXISTS (SELECT 1 FROM MasterSKU WHERE SKUCode='SKU-INDOM-007')
  INSERT INTO MasterSKU (SKUCode,SKUName,CategoryId,UOMId,Barcode,Qty,MinStock,MaxStock,Status,CreatedAt,UpdatedAt)
  VALUES ('SKU-INDOM-007','Indomie Goreng 1 Karton',@catFMCG,@uomCTN,'8901234560007',0,30,300,'Active',GETUTCDATE(),GETUTCDATE());
IF NOT EXISTS (SELECT 1 FROM MasterSKU WHERE SKUCode='SKU-TEPUN-008')
  INSERT INTO MasterSKU (SKUCode,SKUName,CategoryId,UOMId,Barcode,Qty,MinStock,MaxStock,Status,CreatedAt,UpdatedAt)
  VALUES ('SKU-TEPUN-008','Tepung Terigu Segitiga 25KG',@catRaw,@uomKG,'8901234560008',0,20,200,'Active',GETUTCDATE(),GETUTCDATE());
IF NOT EXISTS (SELECT 1 FROM MasterSKU WHERE SKUCode='SKU-KRDUS-009')
  INSERT INTO MasterSKU (SKUCode,SKUName,CategoryId,UOMId,Barcode,Qty,MinStock,MaxStock,Status,CreatedAt,UpdatedAt)
  VALUES ('SKU-KRDUS-009','Kardus Box Large 60x40x40',@catPack,@uomPCS,'8901234560009',0,100,1000,'Active',GETUTCDATE(),GETUTCDATE());
IF NOT EXISTS (SELECT 1 FROM MasterSKU WHERE SKUCode='SKU-SPEAK-010')
  INSERT INTO MasterSKU (SKUCode,SKUName,CategoryId,UOMId,Barcode,Qty,MinStock,MaxStock,Status,CreatedAt,UpdatedAt)
  VALUES ('SKU-SPEAK-010','Speaker JBL Charge 5 Portable',@catElec,@uomPCS,'8901234560010',0,5,80,'Active',GETUTCDATE(),GETUTCDATE());
"@
Run-SQL $skuSQL | Out-Null
Log-OK "Master SKU seeded (10 SKUs)"

# ============================================================
Log-HEAD "STEP 2 - AUTH: LOGIN SEMUA USER"
# ============================================================

try {
    $loginAdmin = Invoke-API -Method POST -Path "/auth/login" -Body @{ username="admin"; password="Admin@123" }
    $tokenAdmin = $loginAdmin.token
    $adminId    = [int]$loginAdmin.userId
    Log-OK "Login admin OK - userId=$adminId role=$($loginAdmin.role)"
} catch { Log-FAIL "Login admin: $_"; exit 1 }

try {
    $loginMgr  = Invoke-API -Method POST -Path "/auth/login" -Body @{ username="manager"; password="Manager@123" }
    $managerId = [int]$loginMgr.userId
    Log-OK "Login manager OK - userId=$managerId role=$($loginMgr.role)"
} catch { Log-FAIL "Login manager: $_" }

try {
    $loginStaff = Invoke-API -Method POST -Path "/auth/login" -Body @{ username="staff"; password="Staff@123" }
    $staffId    = [int]$loginStaff.userId
    Log-OK "Login staff OK - userId=$staffId role=$($loginStaff.role)"
} catch { Log-FAIL "Login staff: $_" }

# ============================================================
Log-HEAD "STEP 3 - GET MASTER DATA VIA API"
# ============================================================

try {
    $uomList = Invoke-API -Method GET -Path "/inventory/uom"
    $uomPCS  = ($uomList | Where-Object { $_.uomCode -eq "PCS" }).id
    $uomKG   = ($uomList | Where-Object { $_.uomCode -eq "KG" }).id
    $uomCTN  = ($uomList | Where-Object { $_.uomCode -eq "CTN" }).id
    $uomLTR  = ($uomList | Where-Object { $_.uomCode -eq "LTR" }).id
    Log-OK "GET /inventory/uom - $($uomList.Count) UOM | PCS=$uomPCS KG=$uomKG CTN=$uomCTN LTR=$uomLTR"
} catch { Log-FAIL "GET /inventory/uom: $_" }

try {
    $bins = Invoke-API -Method GET -Path "/binlocation"
    Log-OK "GET /binlocation - $($bins.Count) bin locations"
    $bins | ForEach-Object { Log-INFO "$($_.binCode) | Zone $($_.zone) | Rack $($_.rack)" }
} catch { Log-FAIL "GET /binlocation: $_" }

try {
    $skus = Invoke-API -Method GET -Path "/mastersku"
    Log-OK "GET /mastersku - $($skus.Count) SKUs"
    $skus | ForEach-Object { Log-INFO "$($_.skuCode) - $($_.skuName)" }
} catch { Log-FAIL "GET /mastersku: $_" }

# ============================================================
Log-HEAD "STEP 4 - RECEIVING (3 Purchase Orders)"
# ============================================================

$pallets1 = @(); $pallets2 = @(); $pallets3 = @()

# Receiving #1 - Electronics
try {
    $rcv1 = Invoke-API -Method POST -Path "/receiving" -Body @{
        supplierName      = "PT. Teknologi Maju Nusantara"
        driverName        = "Budi Santoso"
        vehicleNumber     = "B 1234 XYZ"
        poNumber          = "PO-2026-0101"
        warehouseLocation = "Gudang Utama Jakarta"
        referenceNumber   = "REF-TKM-001"
        notes             = "Handle with care - electronic goods"
        receivedBy        = $staffId
        details           = @(
            @{ skuCode="SKU-LAPTOP-001"; qty=15; uomId=$uomPCS },
            @{ skuCode="SKU-MOUSE-002";  qty=80; uomId=$uomPCS },
            @{ skuCode="SKU-KEYBD-003";  qty=60; uomId=$uomPCS }
        )
    }
    $pallets1 = $rcv1.pallets
    Log-OK "POST /receiving - $($rcv1.receivingNumber) | $($pallets1.Count) pallets"
    $pallets1 | ForEach-Object { Log-INFO "Pallet $($_.palletId) | $($_.skuNumber) qty=$($_.qty)" }
} catch { Log-FAIL "Receiving #1 Electronics: $_" }

# Receiving #2 - FMCG
try {
    $rcv2 = Invoke-API -Method POST -Path "/receiving" -Body @{
        supplierName      = "PT. Distribusi Pangan Sejahtera"
        driverName        = "Ahmad Fauzi"
        vehicleNumber     = "D 5678 ABC"
        poNumber          = "PO-2026-0102"
        warehouseLocation = "Gudang Utama Jakarta"
        referenceNumber   = "REF-DPS-001"
        notes             = "FMCG - simpan suhu ruang"
        receivedBy        = $staffId
        details           = @(
            @{ skuCode="SKU-MINYAK-005"; qty=120; uomId=$uomLTR },
            @{ skuCode="SKU-GULA-006";   qty=150; uomId=$uomKG  },
            @{ skuCode="SKU-INDOM-007";  qty=80;  uomId=$uomCTN }
        )
    }
    $pallets2 = $rcv2.pallets
    Log-OK "POST /receiving - $($rcv2.receivingNumber) | $($pallets2.Count) pallets"
    $pallets2 | ForEach-Object { Log-INFO "Pallet $($_.palletId) | $($_.skuNumber) qty=$($_.qty)" }
} catch { Log-FAIL "Receiving #2 FMCG: $_" }

# Receiving #3 - Raw Material & Packaging
try {
    $rcv3 = Invoke-API -Method POST -Path "/receiving" -Body @{
        supplierName      = "PT. Mandiri Packaging Indonesia"
        driverName        = "Slamet Riyadi"
        vehicleNumber     = "F 9012 DEF"
        poNumber          = "PO-2026-0103"
        warehouseLocation = "Gudang Utama Jakarta"
        referenceNumber   = "REF-MPI-001"
        notes             = "Raw material dan packaging supplies"
        receivedBy        = $staffId
        details           = @(
            @{ skuCode="SKU-TEPUN-008"; qty=100; uomId=$uomKG  },
            @{ skuCode="SKU-KRDUS-009"; qty=200; uomId=$uomPCS },
            @{ skuCode="SKU-SPEAK-010"; qty=30;  uomId=$uomPCS }
        )
    }
    $pallets3 = $rcv3.pallets
    Log-OK "POST /receiving - $($rcv3.receivingNumber) | $($pallets3.Count) pallets"
    $pallets3 | ForEach-Object { Log-INFO "Pallet $($_.palletId) | $($_.skuNumber) qty=$($_.qty)" }
} catch { Log-FAIL "Receiving #3 Raw/Pack: $_" }

try {
    $rcvHistory = Invoke-API -Method GET -Path "/receiving"
    Log-OK "GET /receiving - $($rcvHistory.Count) receiving orders total"
} catch { Log-FAIL "GET /receiving: $_" }

# ============================================================
Log-HEAD "STEP 5 - CEK PENDING PUTAWAY"
# ============================================================

try {
    $pending = Invoke-API -Method GET -Path "/putaway/pending"
    Log-OK "GET /putaway/pending - $($pending.Count) pallets menunggu putaway"
} catch { Log-FAIL "GET /putaway/pending: $_" }

# ============================================================
Log-HEAD "STEP 6 - PUTAWAY: Tempatkan semua pallet ke bin"
# ============================================================

$allPallets = @()
if ($pallets1.Count -gt 0) { $allPallets += $pallets1 }
if ($pallets2.Count -gt 0) { $allPallets += $pallets2 }
if ($pallets3.Count -gt 0) { $allPallets += $pallets3 }

$binCodes = @("A-01-001","A-01-002","A-02-001","A-02-002","A-03-001","B-01-001","B-01-002","B-02-001","B-02-002","C-01-001","C-01-002","C-02-001")
$binIdx   = 0
$putSucc  = 0
$putFail  = 0
$lastPallet = ""

foreach ($pallet in $allPallets) {
    $bin = $binCodes[$binIdx % $binCodes.Count]
    $binIdx++
    try {
        $res = Invoke-API -Method POST -Path "/putaway/confirm" -Body @{
            palletId    = $pallet.palletId
            binCode     = $bin
            confirmedBy = $staffId
        }
        Log-OK "Putaway $($pallet.palletId) ($($pallet.skuNumber) x$($pallet.qty)) => $bin"
        $putSucc++
        $lastPallet = $pallet.palletId
    } catch {
        Log-FAIL "Putaway $($pallet.palletId): $_"
        $putFail++
    }
}
Log-INFO "Putaway selesai: $putSucc berhasil, $putFail gagal dari $($allPallets.Count) total"

# ============================================================
Log-HEAD "STEP 7 - CEK INVENTORY STOCK"
# ============================================================

try {
    $inventory = Invoke-API -Method GET -Path "/inventory"
    Log-OK "GET /inventory - $($inventory.Count) stock records"
    $inventory | ForEach-Object {
        Log-INFO "$($_.skuNumber) | $($_.skuName) | Pallet=$($_.palletId) | Bin=$($_.binLocation) | Qty=$($_.quantity) $($_.uom)"
    }
} catch { Log-FAIL "GET /inventory: $_"; $inventory = @() }

try {
    $invBySku = Invoke-API -Method GET -Path "/inventory/by-code/SKU-LAPTOP-001"
    $cnt = if ($invBySku -is [array]) { $invBySku.Count } else { 1 }
    Log-OK "GET /inventory/by-code/SKU-LAPTOP-001 - $cnt records"
} catch { Log-FAIL "GET /inventory/by-code: $_" }

# ============================================================
Log-HEAD "STEP 8 - CEK STOCK POSISI PER PALLET"
# ============================================================

if ($lastPallet -ne "") {
    try {
        $stockPos = Invoke-API -Method GET -Path "/putaway/stock/$lastPallet"
        Log-OK "GET /putaway/stock/$lastPallet - SKU=$($stockPos.skuCode) Qty=$($stockPos.qty) Bin=$($stockPos.binCode) Status=$($stockPos.status)"
    } catch { Log-FAIL "GET /putaway/stock/$lastPallet : $_" }
}

# ============================================================
Log-HEAD "STEP 9 - INVENTORY ADJUSTMENT (Create, Approve, Reject)"
# ============================================================

# Adjustment #1 - akan di-APPROVE
$adj1Id = $null
try {
    $adj1 = Invoke-API -Method POST -Path "/inventoryadjustment" -Body @{
        skuCode        = "SKU-LAPTOP-001"
        newQty         = 45
        adjustmentType = "Correction"
        reason         = "Physical count discrepancy saat stock opname Q1 2026"
        remarks        = "Stock opname Q1 2026"
        requestedBy    = $staffId
    }
    $adj1Id = [int]$adj1.adjustmentId
    Log-OK "POST /inventoryadjustment - ID=$adj1Id No=$($adj1.adjustmentNo) SKU=SKU-LAPTOP-001 PrevQty=$($adj1.prevQty) NewQty=45 Status=Pending"
} catch { Log-FAIL "Create Adjustment #1: $_" }

# Adjustment #2 - akan di-REJECT
$adj2Id = $null
try {
    $adj2 = Invoke-API -Method POST -Path "/inventoryadjustment" -Body @{
        skuCode        = "SKU-MOUSE-002"
        newQty         = 5
        adjustmentType = "Damage"
        reason         = "Barang rusak akibat kebocoran air di gudang"
        remarks        = "Incident gudang 17 Mei 2026"
        requestedBy    = $staffId
    }
    $adj2Id = [int]$adj2.adjustmentId
    Log-OK "POST /inventoryadjustment - ID=$adj2Id No=$($adj2.adjustmentNo) SKU=SKU-MOUSE-002 PrevQty=$($adj2.prevQty) NewQty=5 Status=Pending"
} catch { Log-FAIL "Create Adjustment #2: $_" }

# GET all adjustments
try {
    $allAdj = Invoke-API -Method GET -Path "/inventoryadjustment"
    Log-OK "GET /inventoryadjustment - $($allAdj.Count) adjustment records"
    $allAdj | ForEach-Object {
        Log-INFO "$($_.adjustmentNo) | $($_.skuNumber) | Prev=$($_.prevQty) New=$($_.newQty) | Status=$($_.approvalStatus)"
    }
} catch { Log-FAIL "GET /inventoryadjustment: $_" }

# APPROVE Adjustment #1
if ($adj1Id -gt 0) {
    try {
        $approveRes = Invoke-API -Method POST -Path "/inventoryadjustment/$adj1Id/approve" -Body @{
            approvedBy = $managerId
        }
        Log-OK "POST /inventoryadjustment/$adj1Id/approve - APPROVED | $($approveRes.message)"
    } catch { Log-FAIL "Approve Adjustment #1 (ID=$adj1Id): $_" }
}

# REJECT Adjustment #2
if ($adj2Id -gt 0) {
    try {
        $rejectRes = Invoke-API -Method POST -Path "/inventoryadjustment/$adj2Id/reject" -Body @{
            rejectedBy      = $managerId
            rejectionReason = "Data tidak cukup - minta foto dan laporan insiden lengkap terlebih dahulu"
        }
        Log-OK "POST /inventoryadjustment/$adj2Id/reject - REJECTED | $($rejectRes.message)"
    } catch { Log-FAIL "Reject Adjustment #2 (ID=$adj2Id): $_" }
}

# ============================================================
Log-HEAD "STEP 10 - VERIFIKASI DATABASE FINAL"
# ============================================================

$cntBins    = (Run-SQL "SELECT COUNT(*) FROM BinLocations WHERE IsActive=1").Trim()
$cntSKUs    = (Run-SQL "SELECT COUNT(*) FROM MasterSKU").Trim()
$cntUsers   = (Run-SQL "SELECT COUNT(*) FROM Users WHERE IsActive=1").Trim()
$cntRcv     = (Run-SQL "SELECT COUNT(*) FROM ReceivingHeader").Trim()
$cntPallets = (Run-SQL "SELECT COUNT(*) FROM ReceivingDetail").Trim()
$cntStock   = (Run-SQL "SELECT COUNT(*) FROM InventoryStock").Trim()
$cntMoves   = (Run-SQL "SELECT COUNT(*) FROM StockMovements").Trim()
$cntAdj     = (Run-SQL "SELECT COUNT(*) FROM InventoryAdjustments").Trim()
$cntAdjApp  = (Run-SQL "SELECT COUNT(*) FROM InventoryAdjustments WHERE ApprovalStatus='Approved'").Trim()
$cntAdjRej  = (Run-SQL "SELECT COUNT(*) FROM InventoryAdjustments WHERE ApprovalStatus='Rejected'").Trim()

Write-Host ""
Write-Host "  DATABASE SUMMARY:" -ForegroundColor Magenta
Write-Host "  +--------------------------+-------+"
Write-Host ("  | Active Bin Locations     | {0,5} |" -f $cntBins)
Write-Host ("  | Master SKUs              | {0,5} |" -f $cntSKUs)
Write-Host ("  | Active Users             | {0,5} |" -f $cntUsers)
Write-Host ("  | Receiving Orders         | {0,5} |" -f $cntRcv)
Write-Host ("  | Total Pallets            | {0,5} |" -f $cntPallets)
Write-Host ("  | Inventory Stock Records  | {0,5} |" -f $cntStock)
Write-Host ("  | Stock Movements Audit    | {0,5} |" -f $cntMoves)
Write-Host ("  | Adjustments Total        | {0,5} |" -f $cntAdj)
Write-Host ("  | Adjustments Approved     | {0,5} |" -f $cntAdjApp)
Write-Host ("  | Adjustments Rejected     | {0,5} |" -f $cntAdjRej)
Write-Host "  +--------------------------+-------+"

# ============================================================
Log-HEAD "HASIL AKHIR E2E TEST"
# ============================================================
$total = $global:OK + $global:FAIL
Write-Host ""
if ($global:FAIL -eq 0) {
    Write-Host "  SEMUA TEST PASSED: $($global:OK)/$total" -ForegroundColor Green
} else {
    Write-Host "  HASIL: $($global:OK) PASSED, $($global:FAIL) FAILED dari $total test" -ForegroundColor Red
}
Write-Host ""
Write-Host "  Endpoints diuji:" -ForegroundColor White
Write-Host "   POST /api/auth/login (3x - admin/manager/staff)"
Write-Host "   GET  /api/inventory/uom"
Write-Host "   GET  /api/binlocation"
Write-Host "   GET  /api/mastersku"
Write-Host "   POST /api/receiving (3x - Electronics, FMCG, Raw+Pack)"
Write-Host "   GET  /api/receiving"
Write-Host "   GET  /api/putaway/pending"
Write-Host "   POST /api/putaway/confirm (semua pallet)"
Write-Host "   GET  /api/inventory"
Write-Host "   GET  /api/inventory/by-code/SKU-LAPTOP-001"
Write-Host "   GET  /api/putaway/stock/{palletId}"
Write-Host "   POST /api/inventoryadjustment (2x)"
Write-Host "   GET  /api/inventoryadjustment"
Write-Host "   POST /api/inventoryadjustment/{id}/approve"
Write-Host "   POST /api/inventoryadjustment/{id}/reject"
Write-Host ""
