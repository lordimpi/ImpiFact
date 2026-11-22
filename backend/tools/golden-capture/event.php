<?php
// Genera el ApplicationResponse de un evento RADIAN (plantilla 88 de ApiDian) sin llamar a la DIAN, para capturar
// casos de referencia (golden). capture.py lo ejecuta dentro del contenedor php_api de la copia local de ApiDian,
// pasándole este archivo por la entrada estándar.
//
// Un evento RADIAN es el mensaje con que el comprador informa a la DIAN qué hizo con una factura recibida: acuse de
// recibo (030), reclamo (031), recibo del bien o servicio (032) o aceptación expresa (033).
//
// SendEventController::sendeventdata primero descarga de la DIAN el XML de la factura referenciada
// (GetXmlByDocumentKey) y de él toma el número, el prefijo, el CUFE, el tipo de documento y los datos del emisor.
// Este script recibe esos datos ya extraídos en input.json y repite el resto del controlador (líneas 1347-1471, la
// rama de los eventos distintos de 5, la aceptación tácita): arma el emisor (la empresa) y el receptor (el emisor
// de la factura), crea el XML con DocumentTrait::createXML y lo firma con SignEvent, que calcula el CUDE.
//
// Entrada: storage/app/public/golden-script/input.json con now (fecha y hora de Colombia, que la plantilla toma de
// Carbon::now()), event_id, type_rejection_id (opcional), issuer_party (opcional), document_reference {number,
// prefix, uuid, type_document_id} y provider {name, identification_number, email, type_organization_id,
// type_regime_id}.
// Salida: storage/app/public/golden-script/output.xml (el ApplicationResponse firmado).

require '/var/www/html/vendor/autoload.php';
$app = require '/var/www/html/bootstrap/app.php';
$app->make(Illuminate\Contracts\Console\Kernel::class)->bootstrap();

use App\Company;
use App\DocumentReference;
use App\Event;
use App\IssuerParty;
use App\TypeDocument;
use App\TypeRejection;
use App\User;
use Carbon\Carbon;
use ubl21dian\XAdES\SignEvent;

final class GoldenEvent
{
    use App\Traits\DocumentTrait;

    public function build(array $data)
    {
        return $this->createXML($data);
    }

    public function checkDigit($nit)
    {
        return $this->validarDigVerifDIAN($nit);
    }
}

$dir = '/var/www/html/storage/app/public/golden-script';
$input = json_decode(file_get_contents("$dir/input.json"), true, 512, JSON_THROW_ON_ERROR);

// La plantilla escribe la fecha y la hora del evento con Carbon::now() (zona America/Bogota): se fijan para que la
// salida sea siempre la misma.
Carbon::setTestNow(Carbon::parse($input['now'], 'America/Bogota'));

$company = Company::where('identification_number', $input['company_nit'])->firstOrFail();
$user = $company->user;
$golden = new GoldenEvent;

$typeDocument = TypeDocument::findOrFail(8);
$event = Event::findOrFail($input['event_id']);

// Emisor del evento: la empresa (el comprador de la factura), como en el controlador.
$senderAll = collect();
$senderAll['name'] = $user->name;
$senderAll['email'] = $user->email;
$senderAll['identification_number'] = $company->identification_number;
$senderAll['dv'] = $company->dv;
$senderAll['tax_id'] = $company->tax_id;
$senderAll['type_organization_id'] = $company->type_organization_id;
$senderAll['type_regime_id'] = $company->type_regime_id;
$senderAll['type_liability_id'] = $company->type_liability_id;
$sender = new User($senderAll->toArray());
$sender->company = new Company($senderAll->toArray());

// Receptor del evento: el emisor de la factura. El controlador toma estos datos del XML de la factura.
$provider = $input['provider'];
$userAll = collect();
$userAll['name'] = $provider['name'];
$userAll['identification_number'] = $provider['identification_number'];
$userAll['email'] = $provider['email'];
$userAll['dv'] = $golden->checkDigit($provider['identification_number']);
$userAll['tax_id'] = 1;
$userAll['type_organization_id'] = $provider['type_organization_id'];
$userAll['type_regime_id'] = $provider['type_regime_id'];
$userAll['type_liability_id'] = 117;
$user = new User($userAll->toArray());
$user->company = new Company($userAll->toArray());

// Documento referenciado: number es el cbc:ID completo de la factura (prefijo incluido). Como en el controlador,
// type_document_id no es un campo asignable de DocumentReference: el tipo llega a la plantilla por
// $typeDocumentReference.
$reference = $input['document_reference'];
$documentReference = new DocumentReference([
    'number' => $reference['number'],
    'prefix' => $reference['prefix'],
    'uuid' => $reference['uuid'],
]);
$typeDocumentReference = TypeDocument::findOrFail($reference['type_document_id']);

$issuerparty = isset($input['issuer_party']) ? new IssuerParty($input['issuer_party']) : NULL;
$typerejection = isset($input['type_rejection_id']) ? TypeRejection::where('id', $input['type_rejection_id'])->firstOrFail() : NULL;
$notes = NULL;
$request = (object) ['resend_consecutive' => false];
$is_event = true;

$eventXML = $golden->build(compact('user', 'company', 'typeDocument', 'event', 'sender', 'documentReference', 'typeDocumentReference', 'issuerparty', 'typerejection', 'notes', 'request', 'is_event'));

$signEvent = new SignEvent($company->certificate->path, $company->certificate->password);
$signEvent->softwareID = $company->software->identifier;
$signEvent->pin = $company->software->pin;
$signEvent->GuardarEn = "$dir/unsigned.xml";
file_put_contents("$dir/output.xml", $signEvent->sign($eventXML)->xml);
echo "ok\n";
