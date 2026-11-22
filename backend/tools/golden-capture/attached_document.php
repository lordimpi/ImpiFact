<?php
// Genera un AttachedDocument (contenedor electrónico, plantilla 89 de ApiDian) sin llamar a la DIAN, para
// capturar casos de referencia (golden). capture.py lo ejecuta dentro del contenedor php_api de la copia
// local de ApiDian, pasándole este archivo por la entrada estándar.
//
// El AttachedDocument es el XML que el emisor entrega al comprador después de que la DIAN acepta una
// factura o una nota: lleva el documento firmado y el ApplicationResponse (la respuesta de validación de la
// DIAN, firmada por ella) dentro de secciones CDATA (texto que el lector XML no interpreta).
//
// Repite los pasos de InvoiceController (líneas 617-650) después de que la DIAN responde IsValid = true:
// lee IssueDate e IssueTime del ApplicationResponse, arma el XML con DocumentTrait::createXML y lo firma
// con SignAttachedDocument. La respuesta de la DIAN es un ApplicationResponse inventado que llega en la
// entrada; el certificado y la empresa son los que configuró capture.py.
//
// Entrada: storage/app/public/golden-script/{input.json, signed-document.xml, application-response.xml}.
// Salida: storage/app/public/golden-script/output.xml (el AttachedDocument firmado).

require '/var/www/html/vendor/autoload.php';
$app = require '/var/www/html/bootstrap/app.php';
$app->make(Illuminate\Contracts\Console\Kernel::class)->bootstrap();

use App\Company;
use App\TypeDocument;
use App\User;
use Carbon\Carbon;
use ubl21dian\XAdES\SignAttachedDocument;

final class GoldenAttachedDocument
{
    use App\Traits\DocumentTrait;

    public function build(array $data)
    {
        return $this->createXML($data);
    }
}

$dir = '/var/www/html/storage/app/public/golden-script';
$input = json_decode(file_get_contents("$dir/input.json"), true, 512, JSON_THROW_ON_ERROR);

// La plantilla escribe la fecha y la hora actuales (Carbon::now(), zona America/Bogota): se fijan para
// que la salida sea siempre la misma.
Carbon::setTestNow(Carbon::parse($input['now'], 'America/Bogota'));

$company = Company::where('identification_number', $input['company_nit'])->firstOrFail();
$user = $company->user;

// Igual que el controlador: el cliente es un User y una Company sin guardar, armados con el JSON del cliente.
$customerAll = collect($input['customer']);
$customer = new User($customerAll->toArray());
$customer->company = new Company($customerAll->toArray());

$resolution = $company->resolutions()->where('type_document_id', $input['type_document_id'])->firstOrFail();
$resolution->number = $input['number'];

$typeDocument = TypeDocument::findOrFail(7);
$cufecude = $input['cufe'];
$document_number = $input['document_number'];
$signedxml = file_get_contents("$dir/signed-document.xml");
$appresponsexml = file_get_contents("$dir/application-response.xml");

$ar = new DOMDocument;
$ar->loadXML($appresponsexml);
$fechavalidacion = $ar->documentElement->getElementsByTagName('IssueDate')->item(0)->nodeValue;
$horavalidacion = $ar->documentElement->getElementsByTagName('IssueTime')->item(0)->nodeValue;

$attacheddocument = (new GoldenAttachedDocument)->build(compact('user', 'company', 'customer', 'resolution', 'typeDocument', 'cufecude', 'signedxml', 'appresponsexml', 'fechavalidacion', 'horavalidacion', 'document_number'));

$signAttachedDocument = new SignAttachedDocument($company->certificate->path, $company->certificate->password);
$signAttachedDocument->GuardarEn = "$dir/signed-by-sign.xml";
file_put_contents("$dir/output.xml", $signAttachedDocument->sign($attacheddocument)->xml);
echo "ok\n";
